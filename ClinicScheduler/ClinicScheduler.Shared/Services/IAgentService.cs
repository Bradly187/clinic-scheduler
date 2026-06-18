using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicScheduler.Shared.Services;

public interface IAgentService
{
    /// <summary>
    /// Processes a chat message using the AI agent.
    /// </summary>
    Task<string> ProcessMessageAsync(JsonArray chatHistory, CancellationToken ct = default);
}
