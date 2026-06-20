using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicScheduler.Web.Services;
using ClinicScheduler.Web.Services.Skills;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

public class AgentServiceTests
{
    private readonly Mock<ISkillRegistry> _mockRegistry;
    private readonly Mock<ISkillExecutor> _mockExecutor;
    private readonly Mock<ClinicScheduler.Core.Interfaces.ICurrentUserService> _mockUserService;
    private readonly IConfiguration _config;

    public AgentServiceTests()
    {
        _mockRegistry = new Mock<ISkillRegistry>();
        _mockExecutor = new Mock<ISkillExecutor>();
        _mockUserService = new Mock<ClinicScheduler.Core.Interfaces.ICurrentUserService>();
        
        var inMemorySettings = new Dictionary<string, string?> {
            {"Gemini:Model", "test-model"},
            {"Gemini:ApiKey", "test-api-key"}
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldInjectSystemPrompt_WhenEmpty()
    {
        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[] { CreateMockResponse("Hello") }));
        var httpClient = new HttpClient(handler);

        _mockRegistry.Setup(r => r.GetSystemPromptCatalog()).Returns("MOCK SYSTEM PROMPT");
        _mockRegistry.Setup(r => r.GetAllSkills()).Returns(Enumerable.Empty<SkillMetadata>());

        var agentService = new AgentService(httpClient, _config, _mockRegistry.Object, _mockExecutor.Object, _mockUserService.Object);
        var chatHistory = new JsonArray();

        var result = await agentService.ProcessMessageAsync(chatHistory);

        result.Should().Be("Hello");
        chatHistory.Count.Should().Be(2); // System prompt + Agent response
        chatHistory[0]?["role"]?.GetValue<string>().Should().Be("system");
        chatHistory[0]?["content"]?.GetValue<string>().Should().Contain("MOCK SYSTEM PROMPT");
    }

    [Fact]
    public async Task ProcessMessageAsync_ShouldExecuteSkillDirectly_AndMakeFollowUpRequest()
    {
        // Skills are loaded upfront so the LLM calls them directly (no load_skill step).
        // Turn 1: LLM calls "test_skill" directly.
        // Turn 2: LLM receives the skill result and replies with the final answer.

        var turn1Response = CreateMockToolCallResponse("test_skill", "{}");
        var turn2Response = CreateMockResponse("Done!");

        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[] { turn1Response, turn2Response }));
        var httpClient = new HttpClient(handler);

        var skillSchema = new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = "test_skill" }
        };

        _mockRegistry.Setup(r => r.GetAllSkills()).Returns(new[] { new SkillMetadata { Name = "test_skill", Description = "A test skill." } });
        _mockExecutor.Setup(e => e.GetToolSchema("test_skill")).Returns(skillSchema);
        _mockExecutor.Setup(e => e.ExecuteAsync("test_skill", It.IsAny<JsonObject?>())).ReturnsAsync("skill result");

        var agentService = new AgentService(httpClient, _config, _mockRegistry.Object, _mockExecutor.Object, _mockUserService.Object);
        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = "MOCK SYSTEM PROMPT" },
            new JsonObject { ["role"] = "user", ["content"] = "Use the test skill" }
        };

        var result = await agentService.ProcessMessageAsync(chatHistory);

        result.Should().Be("Done!");
        handler.Requests.Count.Should().Be(2);

        // Both requests should include test_skill in the tools list
        var request1Tools = handler.Requests[0]["tools"]?.AsArray();
        request1Tools.Should().NotBeNull();
        request1Tools!.Count.Should().Be(1);
        request1Tools[0]?["function"]?["name"]?.GetValue<string>().Should().Be("test_skill");

        // Second request should also carry the same tools
        var request2Tools = handler.Requests[1]["tools"]?.AsArray();
        request2Tools.Should().NotBeNull();
        request2Tools!.Count.Should().Be(1);
        request2Tools[0]?["function"]?["name"]?.GetValue<string>().Should().Be("test_skill");
    }

    // Helper methods to create mocked Gemini (OpenAI-compatible) responses
    private JsonObject CreateMockResponse(string content)
    {
        return new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["finish_reason"] = "stop",
                    ["message"] = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = content
                    }
                }
            }
        };
    }

    private JsonObject CreateMockToolCallResponse(string functionName, string arguments)
    {
        return new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["finish_reason"] = "tool_calls",
                    ["message"] = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = "",
                        ["tool_calls"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "call_123",
                                ["type"] = "function",
                                ["function"] = new JsonObject
                                {
                                    ["name"] = functionName,
                                    ["arguments"] = arguments
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}

// A custom HTTP handler to intercept requests and return predefined mock responses
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<JsonObject> _responses;
    public List<JsonObject> Requests { get; } = new();

    public MockHttpMessageHandler(Queue<JsonObject> responses)
    {
        _responses = responses;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content != null)
        {
            var contentStr = await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(JsonSerializer.Deserialize<JsonObject>(contentStr)!);
        }

        if (_responses.Count == 0)
        {
            throw new Exception("No more mocked responses available in the queue.");
        }

        var responseJson = _responses.Dequeue();

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
        };
    }
}
