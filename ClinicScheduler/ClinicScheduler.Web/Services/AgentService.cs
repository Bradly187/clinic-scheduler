using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Shared.Services;
using ClinicScheduler.Web.Services.Skills;

namespace ClinicScheduler.Web.Services;

public class AgentService : IAgentService
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;
    private readonly string _endpoint;
    private readonly ISkillRegistry _skillRegistry;
    private readonly ISkillExecutor _skillExecutor;
    private readonly ICurrentUserService _currentUserService;

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

        _modelName = config["Ollama:Model"] ?? "llama3.1";
        _endpoint = config["Ollama:Endpoint"] ?? "http://localhost:11434/v1/chat/completions";
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<string> ProcessMessageAsync(JsonArray chatHistory, CancellationToken ct = default)
    {
        var tools = new JsonArray();
        foreach (var skill in _skillRegistry.GetAllSkills())
        {
            tools.Add(_skillExecutor.GetToolSchema(skill.Name));
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

        var response = await SendRequestAsync(requestBody, ct);

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
            throw new Exception($"Ollama API Error: {res.StatusCode} - {err}");
        }

        var resString = await res.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonObject>(resString);
    }
}

