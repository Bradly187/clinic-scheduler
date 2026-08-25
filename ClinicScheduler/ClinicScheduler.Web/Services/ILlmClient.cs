using System.Text.Json.Nodes;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Abstraction over an LLM provider that speaks the OpenAI chat completions format.
/// Both Gemini and Bedrock implementations translate to/from this common format
/// so the tool-calling loop in <see cref="AgentService"/> remains provider-agnostic.
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a chat completion request and returns the response in OpenAI format.
    /// The request body must contain "model", "messages", and "tools" keys.
    /// The response must contain a "choices" array with "message" and "finish_reason".
    /// </summary>
    Task<JsonObject?> SendChatCompletionAsync(JsonObject requestBody, CancellationToken ct = default);
}
