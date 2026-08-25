using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Microsoft.Extensions.Logging;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// LLM client that sends requests to AWS Bedrock Converse API and translates
/// to/from the OpenAI chat completions format expected by <see cref="AgentService"/>.
/// </summary>
public sealed class BedrockLlmClient : ILlmClient
{
    private static readonly ActivitySource ActivitySource = new("ClinicScheduler.AgentService");

    private readonly AmazonBedrockRuntimeClient _client;
    private readonly ILogger<BedrockLlmClient> _logger;

    public BedrockLlmClient(IConfiguration config, ILogger<BedrockLlmClient> logger)
    {
        var region = Amazon.RegionEndpoint.GetBySystemName(
            config["Agent:Region"] ?? config["AWS:Region"] ?? "us-east-1");
        _client = new AmazonBedrockRuntimeClient(region);
        _logger = logger;
    }

    public async Task<JsonObject?> SendChatCompletionAsync(JsonObject requestBody, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("SendRequestToBedrock");

        var modelId = requestBody["model"]?.GetValue<string>() ?? "anthropic.claude-sonnet-4-20250514-v1:0";
        var messages = requestBody["messages"]?.AsArray();
        var tools = requestBody["tools"]?.AsArray();

        if (messages is null || messages.Count == 0)
            return null;

        _logger.LogDebug("Sending Converse request to Bedrock model {Model}", modelId);

        var request = new ConverseRequest
        {
            ModelId = modelId,
            Messages = ConvertMessages(messages),
            InferenceConfig = new InferenceConfiguration { MaxTokens = 4096, Temperature = 0.3F }
        };

        // Extract system prompt
        var systemText = ExtractSystemPrompt(messages);
        if (!string.IsNullOrEmpty(systemText))
        {
            request.System = [new SystemContentBlock { Text = systemText }];
        }

        // Convert tools
        if (tools is not null && tools.Count > 0)
        {
            request.ToolConfig = new ToolConfiguration
            {
                Tools = ConvertTools(tools)
            };
        }

        try
        {
            var response = await _client.ConverseAsync(request, ct);
            _logger.LogDebug("Bedrock responded with StopReason: {StopReason}", response.StopReason);
            return ConvertResponseToOpenAI(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bedrock Converse API error for model {Model}", modelId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static string? ExtractSystemPrompt(JsonArray messages)
    {
        if (messages.Count > 0 && messages[0]?["role"]?.GetValue<string>() == "system")
            return messages[0]?["content"]?.GetValue<string>();
        return null;
    }

    private static List<Message> ConvertMessages(JsonArray messages)
    {
        var result = new List<Message>();

        foreach (var msg in messages)
        {
            var role = msg?["role"]?.GetValue<string>();
            if (role == "system") continue; // handled separately

            var bedrockRole = role switch
            {
                "user" => ConversationRole.User,
                "assistant" => ConversationRole.Assistant,
                "tool" => ConversationRole.User, // tool results go as user messages in Bedrock
                _ => ConversationRole.User
            };

            var content = new List<ContentBlock>();

            if (role == "tool")
            {
                // Convert tool result to Bedrock format
                var toolCallId = msg?["tool_call_id"]?.GetValue<string>() ?? "";
                var toolContent = msg?["content"]?.GetValue<string>() ?? "";
                content.Add(new ContentBlock
                {
                    ToolResult = new ToolResultBlock
                    {
                        ToolUseId = toolCallId,
                        Content = [new ToolResultContentBlock { Text = toolContent }]
                    }
                });
            }
            else if (role == "assistant" && msg?["tool_calls"] is JsonArray toolCalls && toolCalls.Count > 0)
            {
                // Assistant message with tool calls
                var textContent = msg?["content"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(textContent))
                    content.Add(new ContentBlock { Text = textContent });

                foreach (var tc in toolCalls)
                {
                    var id = tc?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                    var name = tc?["function"]?["name"]?.GetValue<string>() ?? "";
                    var argsStr = tc?["function"]?["arguments"]?.GetValue<string>() ?? "{}";

                    content.Add(new ContentBlock
                    {
                        ToolUse = new ToolUseBlock
                        {
                            ToolUseId = id,
                            Name = name,
                            Input = Amazon.Runtime.Documents.Document.FromObject(
                                JsonSerializer.Deserialize<Dictionary<string, object>>(argsStr) ?? new())
                        }
                    });
                }
            }
            else
            {
                // Plain text message
                var text = msg?["content"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(text))
                    content.Add(new ContentBlock { Text = text });
            }

            if (content.Count > 0)
                result.Add(new Message { Role = bedrockRole, Content = content });
        }

        return result;
    }

    private static List<Tool> ConvertTools(JsonArray tools)
    {
        var result = new List<Tool>();

        foreach (var tool in tools)
        {
            var func = tool?["function"];
            if (func is null) continue;

            var name = func["name"]?.GetValue<string>() ?? "";
            var description = func["description"]?.GetValue<string>() ?? "";
            var parameters = func["parameters"];

            var toolSpec = new ToolSpecification
            {
                Name = name,
                Description = description
            };

            if (parameters is not null)
            {
                toolSpec.InputSchema = new ToolInputSchema
                {
                    Json = Amazon.Runtime.Documents.Document.FromObject(
                        JsonSerializer.Deserialize<Dictionary<string, object>>(parameters.ToJsonString()) ?? new())
                };
            }

            result.Add(new Tool { ToolSpec = toolSpec });
        }

        return result;
    }

    private static JsonObject ConvertResponseToOpenAI(ConverseResponse response)
    {
        var message = new JsonObject { ["role"] = "assistant" };
        string finishReason = "stop";

        if (response.StopReason?.Value == "tool_use")
        {
            finishReason = "tool_calls";
            var toolCalls = new JsonArray();

            foreach (var block in response.Output.Message.Content)
            {
                if (block.ToolUse is not null)
                {
                    string argsJson;
                    try { argsJson = JsonSerializer.Serialize(block.ToolUse.Input); }
                    catch { argsJson = "{}"; }
                    toolCalls.Add(new JsonObject
                    {
                        ["id"] = block.ToolUse.ToolUseId,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = block.ToolUse.Name,
                            ["arguments"] = argsJson
                        }
                    });
                }
                else if (block.Text is not null)
                {
                    message["content"] = block.Text;
                }
            }

            message["tool_calls"] = toolCalls;
        }
        else
        {
            // Text response
            var textContent = "";
            foreach (var block in response.Output.Message.Content)
            {
                if (block.Text is not null)
                    textContent += block.Text;
            }
            message["content"] = textContent;
        }

        return new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["message"] = message,
                    ["finish_reason"] = finishReason
                }
            }
        };
    }
}
