using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// LLM client that sends requests to Google Gemini's OpenAI-compatible endpoint.
/// </summary>
public sealed class GeminiLlmClient : ILlmClient
{
    private static readonly ActivitySource ActivitySource = new("ClinicScheduler.AgentService");

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly ILogger<GeminiLlmClient> _logger;

    public GeminiLlmClient(HttpClient httpClient, IConfiguration config, ILogger<GeminiLlmClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

        var apiKey = config["Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonObject?> SendChatCompletionAsync(JsonObject requestBody, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("SendRequestToGemini");
        _logger.LogDebug("Sending request to Gemini API.");

        var content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        var res = await _httpClient.PostAsync(_endpoint, content, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            _logger.LogError("Gemini API Error: {StatusCode} - {Error}", res.StatusCode, err);
            activity?.SetStatus(ActivityStatusCode.Error, err);
            throw new Exception($"Gemini API Error: {res.StatusCode} - {err}");
        }

        var resString = await res.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("Received successful response from Gemini API.");
        return JsonSerializer.Deserialize<JsonObject>(resString);
    }
}
