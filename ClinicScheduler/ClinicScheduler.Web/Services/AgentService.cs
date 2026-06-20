using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
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
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly string _endpoint;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillExecutor _skillExecutor;
    private readonly ICurrentUserService _currentUserService;

    /// <summary>
    /// Initializes the AgentService with required dependencies and configures
    /// the Google Gemini endpoint and API key from application settings.
    /// </summary>
    public AgentService(
        HttpClient httpClient,
        IConfiguration config,
        ISkillRegistry skillRegistry,
        ISkillExecutor skillExecutor,
        ICurrentUserService currentUserService)
    {
        _httpClient = httpClient;
        _skillRegistry = skillRegistry;
        _skillExecutor = skillExecutor;
        _currentUserService = currentUserService;

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
            catch (ArgumentException)
            {
                // Described in markdown but not implemented in SkillExecutor; ignore it.
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
        }

        var requestBody = new JsonObject
        {
            ["model"] = _modelName,
            ["messages"] = chatHistory.DeepClone(),
            ["tools"] = tools
        };

        // Initial request to the Gemini API
        var response = await SendRequestAsync(requestBody, ct);

        // Tool execution loop: process tool calls until the LLM returns a final text response
        while (response != null)
        {
            var choice = response["choices"]?[0];
            if (choice == null) break;

            var finishReason = choice["finish_reason"]?.GetValue<string>();
            var message = choice["message"]?.AsObject();

            if (message == null) break;

            chatHistory.Add(message.DeepClone());

            if (finishReason == "tool_calls" || message.ContainsKey("tool_calls"))
            {
                var toolCalls = message["tool_calls"]?.AsArray();
                if (toolCalls != null)
                {
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
                            var resultText = await _skillExecutor.ExecuteAsync(toolName, toolInput);
                            chatHistory.Add(new JsonObject
                            {
                                ["role"] = "tool",
                                ["name"] = toolName,
                                ["tool_call_id"] = toolUseId,
                                ["content"] = resultText
                            });
                        }
                    }
                }

                requestBody["messages"] = chatHistory.DeepClone();
                requestBody["tools"] = tools;
                response = await SendRequestAsync(requestBody, ct);
            }
            else
            {
                return message["content"]?.GetValue<string>() ?? "No response from assistant.";
            }
        }

        return "No response from assistant.";
    }

    private async Task<JsonObject?> SendRequestAsync(JsonObject requestBody, CancellationToken ct)
    {
        var content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        var res = await _httpClient.PostAsync(_endpoint, content, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API Error: {res.StatusCode} - {err}");
        }

        var resString = await res.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonObject>(resString);
    }
}

