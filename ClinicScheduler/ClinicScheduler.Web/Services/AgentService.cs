using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Shared.Services;
using ClinicScheduler.Web.Services.Skills;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Core Agent Service responsible for orchestrating interactions between the user,
/// the Google Gemini API (via its OpenAI-compatible endpoint), and the backend skills (tools).
/// It handles tool execution loops, context injection, and API communication.
/// </summary>
public class AgentService : IAgentService
{
    private static readonly ActivitySource ActivitySource = new("ClinicScheduler.AgentService");

    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly string _endpoint;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillExecutor _skillExecutor;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AgentService> _logger;

    /// <summary>
    /// Initializes the AgentService with required dependencies and configures
    /// the Google Gemini endpoint and API key from application settings.
    /// </summary>
    public AgentService(
        HttpClient httpClient,
        IConfiguration config,
        ISkillRegistry skillRegistry,
        ISkillExecutor skillExecutor,
        ICurrentUserService currentUserService,
        ILogger<AgentService> logger)
    {
        _httpClient = httpClient;
        _skillRegistry = skillRegistry;
        _skillExecutor = skillExecutor;
        _currentUserService = currentUserService;
        _logger = logger;

        _modelName = config["Gemini:Model"];
        if (string.IsNullOrWhiteSpace(_modelName)) _modelName = "gemini-2.5-flash";

        _endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

        var apiKey = config["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Processes a chat message loop. It injects available tool schemas, handles tool-call responses, 
    /// executes the requested tools (skills), and returns the final natural language response.
    /// </summary>
    /// <param name="chatHistory">The conversation history (JSON array of OpenAI format messages).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The LLM's final response string.</returns>
    public async Task<string> ProcessMessageAsync(JsonArray chatHistory, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("ProcessMessageAsync");
        _logger.LogInformation("Starting ProcessMessageAsync. History length: {Count}", chatHistory.Count);

        // Load available tools dynamically from the SkillRegistry (C# Skill implementations).
        // A SKILL.md folder that has no matching schema in SkillExecutor would otherwise
        // throw and break the entire chat — skip those skills instead of failing hard.
        var tools = new JsonArray();
        foreach (var skill in _skillRegistry.GetAllSkills())
        {
            try
            {
                tools.Add(_skillExecutor.GetToolSchema(skill.Name));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Skill {SkillName} is described but not implemented in SkillExecutor. Skipping.", skill.Name);
            }
        }
        
        // Inject system prompt if not present
        if (chatHistory.Count == 0 || chatHistory[0]?["role"]?.GetValue<string>() != "system")
        {
            var systemPrompt = _skillRegistry.GetSystemPromptCatalog();

            // Append current user info
            var user = _currentUserService.Principal;
            string roleInfo = "Guest";
            string userNameInfo = "Anonymous";
            if (user != null && user.Identity?.IsAuthenticated == true)
            {
                userNameInfo = user.Identity.Name ?? "Unknown";
                var roles = new List<string>();
                foreach (var role in new[] { "Admin", "ClinicManager", "Therapist", "Staff", "Patient" })
                {
                    if (user.IsInRole(role)) roles.Add(role);
                }
                roleInfo = roles.Count > 0 ? string.Join(", ", roles) : "Authenticated User";
            }

            systemPrompt += $"\nCurrently logged in user: {userNameInfo} (Roles: {roleInfo})\n";

            chatHistory.Insert(0, new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            });
            _logger.LogDebug("Injected system prompt for user {UserName} with roles {Roles}", userNameInfo, roleInfo);
        }

        // Run the shared tool-calling loop, executing skills via the SkillExecutor.
        var result = await RunLoopAsync(chatHistory, tools,
            async (toolName, toolInput, innerCt) => 
            {
                using var toolActivity = ActivitySource.StartActivity("ExecuteSkill");
                toolActivity?.SetTag("skill.name", toolName);
                _logger.LogInformation("Executing skill: {SkillName}", toolName);
                
                var res = await _skillExecutor.ExecuteAsync(toolName, toolInput);
                
                _logger.LogInformation("Skill {SkillName} completed.", toolName);
                return res;
            }, ct);

        _logger.LogInformation("ProcessMessageAsync completed.");
        return result;
    }

    /// <summary>
    /// Reusable LLM tool-calling loop. Sends <paramref name="messages"/> and <paramref name="tools"/>
    /// to Gemini and, whenever the model returns tool calls, dispatches each through
    /// <paramref name="executeTool"/> and feeds the results back — repeating until the model produces
    /// a final text answer or the iteration cap is reached. The <paramref name="messages"/> array is
    /// mutated in place (assistant + tool turns are appended).
    ///
    /// This single primitive powers both the single-agent flow (skills as tools) and the
    /// multi-agent <see cref="OrchestratorAgentService"/> (specialist agents as tools).
    /// </summary>
    /// <param name="messages">Conversation so far, including the system prompt as the first entry.</param>
    /// <param name="tools">Tool schemas to offer the model.</param>
    /// <param name="executeTool">Callback that runs a tool by name and returns its textual result.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> RunLoopAsync(
        JsonArray messages,
        JsonArray tools,
        Func<string, JsonObject?, CancellationToken, Task<string>> executeTool,
        CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("RunLoopAsync");
        _logger.LogInformation("Entering LLM Tool Calling Loop. Available tools: {ToolCount}", tools.Count);

        var requestBody = new JsonObject
        {
            ["model"] = _modelName,
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools
        };

        var response = await SendRequestAsync(requestBody, ct);

        // Cap tool-call rounds so a misbehaving model can't loop forever.
        const int maxToolRounds = 8;
        var toolRounds = 0;

        while (response != null)
        {
            var choice = response["choices"]?[0];
            if (choice == null) break;

            var finishReason = choice["finish_reason"]?.GetValue<string>();
            var message = choice["message"]?.AsObject();

            if (message == null) break;

            messages.Add(message.DeepClone());

            if (finishReason == "tool_calls" || message.ContainsKey("tool_calls"))
            {
                if (++toolRounds > maxToolRounds)
                {
                    _logger.LogWarning("Exceeded maximum tool rounds ({MaxRounds}). Aborting.", maxToolRounds);
                    return "I wasn't able to complete that request within a reasonable number of steps. " +
                           "Please try rephrasing or breaking it into smaller requests.";
                }

                var toolCalls = message["tool_calls"]?.AsArray();
                if (toolCalls != null)
                {
                    _logger.LogInformation("Model requested {ToolCallCount} tool call(s) (Round {Round}).", toolCalls.Count, toolRounds);
                    foreach (var toolCall in toolCalls)
                    {
                        var toolUseId = toolCall?["id"]?.GetValue<string>();
                        var functionObj = toolCall?["function"]?.AsObject();
                        var toolName = functionObj?["name"]?.GetValue<string>();
                        var toolArgsStr = functionObj?["arguments"]?.GetValue<string>();

                        JsonObject? toolInput = null;
                        if (!string.IsNullOrWhiteSpace(toolArgsStr))
                        {
                            try { toolInput = JsonSerializer.Deserialize<JsonObject>(toolArgsStr); } catch { }
                        }

                        if (toolName != null)
                        {
                            _logger.LogDebug("Dispatching tool call: {ToolName}", toolName);
                            var resultText = await executeTool(toolName, toolInput, ct);
                            messages.Add(new JsonObject
                            {
                                ["role"] = "tool",
                                ["name"] = toolName,
                                ["tool_call_id"] = toolUseId,
                                ["content"] = resultText
                            });
                        }
                    }
                }

                requestBody["messages"] = messages.DeepClone();
                requestBody["tools"] = tools;
                response = await SendRequestAsync(requestBody, ct);
            }
            else
            {
                _logger.LogInformation("Model produced final text response (Round {Round}).", toolRounds);
                return message["content"]?.GetValue<string>() ?? "No response from assistant.";
            }
        }

        _logger.LogWarning("LLM loop terminated without a final content response.");
        return "No response from assistant.";
    }

    private async Task<JsonObject?> SendRequestAsync(JsonObject requestBody, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("SendRequestToGemini");
        _logger.LogDebug("Sending request to Gemini API ({Model}).", _modelName);

        var content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        var res = await _httpClient.PostAsync(_endpoint, content, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync();
            _logger.LogError("Gemini API Error: {StatusCode} - {Error}", res.StatusCode, err);
            activity?.SetStatus(ActivityStatusCode.Error, err);
            throw new Exception($"Gemini API Error: {res.StatusCode} - {err}");
        }

        var resString = await res.Content.ReadAsStringAsync();
        _logger.LogDebug("Received successful response from Gemini API.");
        return JsonSerializer.Deserialize<JsonObject>(resString);
    }
}

