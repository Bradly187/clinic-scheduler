using System.Text.Json.Nodes;
using ClinicScheduler.Web.Services;
using ClinicScheduler.Web.Services.Skills;
using ClinicScheduler.Web.Services.Workflows;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

public class OrchestratorAgentServiceTests
{
    private readonly Mock<ISkillRegistry> _mockRegistry = new();
    private readonly Mock<ISkillExecutor> _mockExecutor = new();
    private readonly Mock<ClinicScheduler.Core.Interfaces.ICurrentUserService> _mockUserService = new();
    private readonly IConfiguration _config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:Model"] = "test-model",
            ["Gemini:ApiKey"] = "test-key"
        }).Build();

    [Fact]
    public async Task ProcessMessageAsync_RoutesToSpecialist_RunsItsToolLoop_AndRelays()
    {
        // The shared HttpClient sees four calls, in order:
        //   1. Coordinator       -> routes to info_agent
        //   2. info_agent        -> calls the get_my_appointments tool
        //   3. info_agent        -> final answer
        //   4. Coordinator       -> relays the final answer
        var responses = new Queue<JsonObject>(new[]
        {
            ToolCall("route_to_info_agent", "{\"task\":\"list my appointments\"}"),
            ToolCall("get_my_appointments", "{}"),
            Text("You have 2 upcoming appointments."),
            Text("You have 2 upcoming appointments.")
        });
        var handler = new MockHttpMessageHandler(responses);
        var httpClient = new HttpClient(handler);

        // Specialist tool schemas + execution come from the orchestrator's SkillExecutor.
        _mockExecutor.Setup(e => e.GetToolSchema(It.IsAny<string>()))
            .Returns((string n) => new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = n } });
        _mockExecutor.Setup(e => e.ExecuteAsync("get_my_appointments", It.IsAny<JsonObject?>()))
            .ReturnsAsync("Upcoming Appointments: 2 found");

        var agent = new AgentService(httpClient, _config, _mockRegistry.Object, _mockExecutor.Object, _mockUserService.Object, NullLogger<AgentService>.Instance);

        // The specialist roster now comes from registered workflow packs (Clinic:Specialty unset -> default).
        var clinicProfile = new ClinicProfile(_config);
        var packs = new IWorkflowPack[]
        {
            new SchedulingWorkflowPack(clinicProfile),
            new TreatmentPlanWorkflowPack(clinicProfile),
            new TriageWorkflowPack(clinicProfile),
        };
        var orchestrator = new OrchestratorAgentService(agent, _mockExecutor.Object, _mockUserService.Object, packs, clinicProfile, NullLogger<OrchestratorAgentService>.Instance);

        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "What appointments do I have?" }
        };

        var result = await orchestrator.ProcessMessageAsync(chatHistory);

        // The coordinator relays the specialist's final answer.
        result.Should().Be("You have 2 upcoming appointments.");

        // Four LLM round-trips: coordinator -> specialist(tool) -> specialist(final) -> coordinator.
        handler.Requests.Count.Should().Be(4);

        // Request 1 (coordinator) is offered the routing tools, not the raw skills.
        var coordinatorTools = handler.Requests[0]["tools"]!.AsArray()
            .Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
        coordinatorTools.Should().Contain("route_to_info_agent");
        coordinatorTools.Should().Contain("route_to_scheduling_agent");
        coordinatorTools.Should().NotContain("get_my_appointments");

        // Request 2 (the specialist) is offered its restricted skills, not the routing tools.
        var specialistTools = handler.Requests[1]["tools"]!.AsArray()
            .Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
        specialistTools.Should().Contain("get_my_appointments");
        specialistTools.Should().NotContain("route_to_info_agent");

        // The specialist actually executed its skill.
        _mockExecutor.Verify(e => e.ExecuteAsync("get_my_appointments", It.IsAny<JsonObject?>()), Times.Once);
    }

    private static JsonObject Text(string content) => new()
    {
        ["choices"] = new JsonArray
        {
            new JsonObject
            {
                ["finish_reason"] = "stop",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content }
            }
        }
    };

    private static JsonObject ToolCall(string name, string arguments) => new()
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
                            ["id"] = $"call_{name}",
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
                        }
                    }
                }
            }
        }
    };
}
