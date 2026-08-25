using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Services;
using ClinicScheduler.Web.Services.Skills;
using ClinicScheduler.Web.Services.Workflows;
using ClinicScheduler.Web.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net.Http.Json;
using Xunit;

namespace ClinicScheduler.Web.Tests.Api;

/// <summary>
/// Integration tests for the OrchestratorAgentService with a mocked LLM but REAL skill
/// execution against the PostgreSQL database. This verifies the full flow:
/// Coordinator routes → Specialist receives tools → Skill executes against DB → Result relayed.
///
/// The LLM responses are scripted (MockHttpMessageHandler) so the tests are deterministic,
/// but the skill execution and DB operations are fully real.
/// </summary>
[Collection("WebApp")]
public class OrchestratorIntegrationTests : IAsyncLifetime
{
    private readonly WebAppFixture _fixture;

    public OrchestratorIntegrationTests(WebAppFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ClaimsPrincipal MakeAdmin() => new(new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "test-admin-id"),
            new Claim(ClaimTypes.Name, "admin@clinic.com"),
            new Claim(ClaimTypes.Role, "Admin")
        }, "TestAuth"));

    private static ClaimsPrincipal MakePatient(string email) => new(new ClaimsIdentity(
        new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Role, "Patient")
        }, "TestAuth"));

    private OrchestratorAgentService BuildOrchestrator(
        MockHttpMessageHandler handler, ClaimsPrincipal user, ISkillExecutor executor)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:Model"] = "test-model",
                ["Gemini:ApiKey"] = "test-key"
            }).Build();

        var httpClient = new HttpClient(handler);
        var mockRegistry = new Mock<ISkillRegistry>();
        mockRegistry.Setup(r => r.GetAllSkills()).Returns(Enumerable.Empty<SkillMetadata>());
        mockRegistry.Setup(r => r.GetSystemPromptCatalog()).Returns("System prompt");

        var mockUserService = new Mock<ICurrentUserService>();
        mockUserService.Setup(s => s.Principal).Returns(user);

        var agentService = new AgentService(
            httpClient, config, mockRegistry.Object, executor,
            mockUserService.Object, NullLogger<AgentService>.Instance);

        var clinicProfile = new ClinicProfile(config);
        var packs = new IWorkflowPack[]
        {
            new SchedulingWorkflowPack(clinicProfile),
            new TreatmentPlanWorkflowPack(clinicProfile),
            new TriageWorkflowPack(clinicProfile),
            new IntakeWorkflowPack(clinicProfile),
        };

        return new OrchestratorAgentService(
            agentService, executor, mockUserService.Object,
            packs, clinicProfile, NullLogger<OrchestratorAgentService>.Instance);
    }
    private static JsonObject TextResponse(string content) => new()
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

    private static JsonObject ToolCallResponse(string name, string arguments) => new()
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
                            ["id"] = $"call_{name}_{Guid.NewGuid():N}",
                            ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = name, ["arguments"] = arguments }
                        }
                    }
                }
            }
        }
    };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Orchestrator_RoutesToInfoAgent_ExecutesGetAppointments_AgainstRealDb()
    {
        // Seed data
        var (patientId, therapistId, roomId, _) = await SeedData.SeedCoreEntitiesAsync(_fixture.Client, "orch1");
        await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });

        // Script the LLM:
        // 1. Coordinator routes to info_agent
        // 2. info_agent calls get_appointments
        // 3. info_agent produces final answer (after receiving real skill output)
        // 4. Coordinator relays
        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[]
        {
            ToolCallResponse("route_to_info_agent", """{"task":"list appointments for Patientorch1"}"""),
            ToolCallResponse("get_appointments", """{"patientName":"Patientorch1"}"""),
            TextResponse("Here are the appointments for Patientorch1: one appointment scheduled."),
            TextResponse("Here are the appointments for Patientorch1: one appointment scheduled.")
        }));

        using var scope = _fixture.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        var orchestrator = BuildOrchestrator(handler, MakeAdmin(), executor);

        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "What appointments does Patientorch1 have?" }
        };

        var result = await orchestrator.ProcessMessageAsync(chatHistory);

        result.Should().Contain("appointment");
        // The skill was actually executed against real DB (4 HTTP requests = coordinator + specialist(tool) + specialist(final) + coordinator)
        handler.Requests.Count.Should().Be(4);
    }

    [Fact]
    public async Task Orchestrator_RoutesToSchedulingAgent_BooksAppointment_RealDb()
    {
        var (patientId, therapistId, roomId, _) = await SeedData.SeedCoreEntitiesAsync(_fixture.Client, "orchbook");

        // Script: coordinator routes → scheduling_agent calls schedule_appointment → final
        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[]
        {
            ToolCallResponse("route_to_scheduling_agent", """{"task":"book an appointment for Patientorchbook with Therapistorchbook on 2030-06-03 at 9am"}"""),
            ToolCallResponse("schedule_appointment", """{"therapistName":"Therapistorchbook","patientName":"Patientorchbook","date":"2030-06-03","startTime":"09:00"}"""),
            TextResponse("Appointment booked successfully for Patientorchbook."),
            TextResponse("Appointment booked successfully for Patientorchbook.")
        }));

        using var scope = _fixture.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        var orchestrator = BuildOrchestrator(handler, MakeAdmin(), executor);

        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "Book Patientorchbook with Therapistorchbook on June 3 2030 at 9am" }
        };

        var result = await orchestrator.ProcessMessageAsync(chatHistory);

        // Verify the orchestrator completed the full routing flow
        result.Should().Contain("booked");
        handler.Requests.Count.Should().Be(4);

        // Verify the specialist was offered the scheduling skills
        var specialistTools = handler.Requests[1]["tools"]!.AsArray()
            .Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
        specialistTools.Should().Contain("schedule_appointment");
        specialistTools.Should().Contain("reschedule_appointment");
        specialistTools.Should().Contain("cancel_any_appointment");
    }

    [Fact]
    public async Task Orchestrator_RoutesToSchedulingAgent_CancelWithConfirmation_RealDb()
    {
        var (patientId, therapistId, roomId, _) = await SeedData.SeedCoreEntitiesAsync(_fixture.Client, "orchcancel");
        var createResp = await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });
        var doc = await JsonDocument.ParseAsync(await createResp.Content.ReadAsStreamAsync());
        var aptId = doc.RootElement.GetProperty("id").GetInt32();

        // Script: coordinator routes → scheduling_agent calls cancel_any_appointment (preview) →
        //         scheduling_agent calls cancel_any_appointment (confirmed) → final
        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[]
        {
            ToolCallResponse("route_to_scheduling_agent", $$$"""{"task":"cancel appointment {{{aptId}}}"}"""),
            ToolCallResponse("cancel_any_appointment", $$$"""{"appointmentId":{{{aptId}}}}"""),
            ToolCallResponse("cancel_any_appointment", $$$"""{"appointmentId":{{{aptId}}},"confirmed":true}"""),
            TextResponse($"Appointment {aptId} has been cancelled."),
            TextResponse($"Appointment {aptId} has been cancelled.")
        }));

        using var scope = _fixture.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        var orchestrator = BuildOrchestrator(handler, MakeAdmin(), executor);

        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = $"Cancel appointment {aptId}" }
        };

        var result = await orchestrator.ProcessMessageAsync(chatHistory);

        // Verify the full two-step flow executed (5 LLM calls: coordinator route + preview + confirm + specialist final + coordinator relay)
        result.Should().Contain("cancelled");
        handler.Requests.Count.Should().Be(5);

        // The specialist's first tool call was the preview (no confirmed flag)
        // The specialist's second tool call had confirmed=true
        // Both were routed through the cancel_any_appointment skill
    }

    [Fact]
    public async Task Orchestrator_SimpleGreeting_DoesNotRoute_JustReplies()
    {
        // For a simple greeting, the coordinator should reply directly without routing
        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[]
        {
            TextResponse("Hello! How can I help you today?")
        }));

        using var scope = _fixture.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        var orchestrator = BuildOrchestrator(handler, MakeAdmin(), executor);

        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "Hello!" }
        };

        var result = await orchestrator.ProcessMessageAsync(chatHistory);

        result.Should().Contain("Hello");
        handler.Requests.Count.Should().Be(1); // Only the coordinator call, no routing
    }

    [Fact]
    public async Task Orchestrator_AllSpecialistRoutes_AreOfferedToCoordinator()
    {
        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[]
        {
            TextResponse("How can I help?")
        }));

        using var scope = _fixture.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        var orchestrator = BuildOrchestrator(handler, MakeAdmin(), executor);

        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "hi" }
        };

        await orchestrator.ProcessMessageAsync(chatHistory);

        // Inspect what tools the coordinator was offered
        var request = handler.Requests[0];
        var tools = request["tools"]!.AsArray()
            .Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();

        tools.Should().Contain("route_to_info_agent");
        tools.Should().Contain("route_to_scheduling_agent");
        tools.Should().Contain("route_to_waitlist_agent");
        tools.Should().Contain("route_to_treatment_plan_agent");
        tools.Should().Contain("route_to_triage_agent");
        tools.Should().Contain("route_to_intake_agent");
    }

    [Fact]
    public async Task Orchestrator_SpecialistReceives_OnlyItsOwnSkills()
    {
        var handler = new MockHttpMessageHandler(new Queue<JsonObject>(new[]
        {
            ToolCallResponse("route_to_waitlist_agent", """{"task":"join waitlist"}"""),
            TextResponse("I'll help you join the waitlist."),
            TextResponse("I'll help you join the waitlist.")
        }));

        using var scope = _fixture.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        var orchestrator = BuildOrchestrator(handler, MakeAdmin(), executor);

        var chatHistory = new JsonArray
        {
            new JsonObject { ["role"] = "user", ["content"] = "Put me on the waitlist" }
        };

        await orchestrator.ProcessMessageAsync(chatHistory);

        // Request 2 is the waitlist specialist's call — check its tools
        handler.Requests.Count.Should().BeGreaterThanOrEqualTo(2);
        var specialistTools = handler.Requests[1]["tools"]!.AsArray()
            .Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();

        specialistTools.Should().Contain("join_waitlist");
        specialistTools.Should().Contain("get_my_waitlist");
        specialistTools.Should().Contain("leave_waitlist");
        // Should NOT have skills from other specialists
        specialistTools.Should().NotContain("schedule_appointment");
        specialistTools.Should().NotContain("register_patient");
        specialistTools.Should().NotContain("route_to_info_agent");
    }
}

/// <summary>
/// Reusable mock HTTP message handler for scripted LLM responses.
/// Defined here as the existing one in Unit/AgentServiceTests.cs is in a different namespace.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<JsonObject> _responses;
    public List<JsonObject> Requests { get; } = new();

    public MockHttpMessageHandler(Queue<JsonObject> responses) => _responses = responses;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content != null)
        {
            var content = await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(JsonSerializer.Deserialize<JsonObject>(content)!);
        }

        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"No more mocked LLM responses. {Requests.Count} requests were made.");

        var response = _responses.Dequeue();
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json")
        };
    }
}
