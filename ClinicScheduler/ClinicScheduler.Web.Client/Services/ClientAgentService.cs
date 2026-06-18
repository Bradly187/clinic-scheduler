using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Client.Services;

public class ClientAgentService : IAgentService
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientAgentService"/> class.
    /// </summary>
    public ClientAgentService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Processes a chat message using the AI agent.
    /// </summary>
    public async Task<string> ProcessMessageAsync(JsonArray chatHistory, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/agent/chat", chatHistory, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AgentChatResponse>(cancellationToken: ct);
        return result?.Response ?? string.Empty;
    }
    
    private class AgentChatResponse
    {
        public string Response { get; set; } = "";
    }
}
