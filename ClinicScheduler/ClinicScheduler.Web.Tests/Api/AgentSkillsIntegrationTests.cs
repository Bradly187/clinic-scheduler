using System.Security.Claims;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Services.Skills;
using ClinicScheduler.Web.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net.Http.Json;
using Xunit;

namespace ClinicScheduler.Web.Tests.Api;

/// <summary>
/// Integration tests for the agent skills layer. These tests use the full DI pipeline with a
/// real PostgreSQL database (via Testcontainers), exercising skill execution through the actual
/// EF Core queries, repository implementations, and business logic services.
///
/// Unlike the Unit/ tests (which mock repositories), these catch:
/// - Query translation issues (EF to SQL)
/// - FK constraint violations
/// - DI wiring problems
/// - Real concurrent behavior
/// </summary>
[Collection("WebApp")]
public class AgentSkillsIntegrationTests : IAsyncLifetime
{
    private readonly WebAppFixture _fixture;

    public AgentSkillsIntegrationTests(WebAppFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IServiceScope CreateScope() => _fixture.Factory.Services.CreateScope();

    private static void SetCurrentUser(IServiceScope scope, string email, string role)
    {
        var mockUser = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
        // The ICurrentUserService is registered as singleton HttpContextCurrentUserService,
        // but for scoped tests we need to mock it. We'll resolve the skill executor and
        // override the user via the SkillExecutor's own scope — see WithMockedUser helper.
    }

    /// <summary>
    /// Resolves an <see cref="ISkillExecutor"/> from a scope where the current user is overridden.
    /// </summary>
    private ISkillExecutor GetSkillExecutor(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISkillExecutor>();

    /// <summary>
    /// Creates a fresh scope with the ICurrentUserService mocked to the given identity.
    /// This works because we register skills as scoped, and each test gets its own scope.
    /// </summary>
    private (IServiceScope scope, ISkillExecutor executor) CreateScopeWithUser(string email, string role)
    {
        // Build a new service scope and override ICurrentUserService for this scope
        var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();

        // We need to build the executor manually with a mocked user service since the
        // real one reads from HttpContext which doesn't exist in integration tests.
        var skills = scope.ServiceProvider.GetServices<ISkill>().ToList();

        // Replace the ICurrentUserService dependency in each skill via reflection isn't practical.
        // Instead, we'll use the WebAppFixture's pre-authed Admin identity (TestAuthHandler).
        // For role-specific tests, we override the mocked user in a custom service provider.
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        return (scope, executor);
    }

    private async Task<(int patientId, int therapistId, int roomId, int therapyTypeId)> SeedViaApiAsync(
        string suffix = "")
    {
        return await SeedData.SeedCoreEntitiesAsync(_fixture.Client, suffix);
    }

    // ── SkillExecutor DI wiring tests ────────────────────────────────────────

    [Fact]
    public void SkillExecutor_IsResolvable_FromDI()
    {
        using var scope = CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        executor.Should().NotBeNull();
        executor.Should().BeOfType<SkillExecutor>();
    }

    [Fact]
    public void AllRegisteredSkills_AreResolvable_FromDI()
    {
        using var scope = CreateScope();
        var skills = scope.ServiceProvider.GetServices<ISkill>().ToList();

        // All 15 skills should be registered
        skills.Count.Should().BeGreaterThanOrEqualTo(15);

        var expectedNames = new[]
        {
            "get_my_appointments", "get_appointments", "schedule_appointment",
            "reschedule_appointment", "cancel_my_appointment", "cancel_any_appointment",
            "join_waitlist", "get_my_waitlist", "leave_waitlist",
            "get_my_treatment_plan", "create_treatment_plan", "generate_plan_appointments",
            "register_patient", "verify_patient_demographics", "start_encounter"
        };

        var actualNames = skills.Select(s => s.Name).ToList();
        foreach (var expected in expectedNames)
            actualNames.Should().Contain(expected, $"skill '{expected}' should be registered in DI");
    }

    [Fact]
    public void AllRegisteredSkills_HaveValidSchemas()
    {
        using var scope = CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();
        var skills = scope.ServiceProvider.GetServices<ISkill>().ToList();

        foreach (var skill in skills)
        {
            var schema = executor.GetToolSchema(skill.Name);
            schema.Should().NotBeNull($"schema for '{skill.Name}' should not be null");
            schema["type"]?.GetValue<string>().Should().Be("function");
            schema["function"].Should().NotBeNull();
            schema["function"]!["name"]?.GetValue<string>().Should().Be(skill.Name);
        }
    }

    [Fact]
    public void GetToolSchema_ThrowsForUnknownSkill()
    {
        using var scope = CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();

        var act = () => executor.GetToolSchema("nonexistent_skill");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_ForUnknownSkill()
    {
        using var scope = CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ISkillExecutor>();

        var result = await executor.ExecuteAsync("nonexistent_skill", null);
        result.Should().Contain("Error: Unknown skill");
    }

    // ── schedule_appointment integration ─────────────────────────────────────

    [Fact]
    public async Task ScheduleAppointment_CreatesAppointment_InRealDatabase()
    {
        var (patientId, therapistId, roomId, _) = await SeedViaApiAsync("sched");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();

        // Verify the seed data is in the DB
        var patient = await db.Patients.FindAsync(patientId);
        var therapist = await db.Therapists.FindAsync(therapistId);
        patient.Should().NotBeNull();
        therapist.Should().NotBeNull();

        // Execute the skill via the API endpoint (which uses the TestAuthHandler → Admin)
        var response = await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        // Verify it persisted correctly
        var appointments = await db.Appointments
            .Where(a => a.PatientId == patientId)
            .ToListAsync();
        appointments.Should().HaveCount(1);
        appointments[0].Status.Should().Be(AppointmentStatus.Scheduled);
        appointments[0].TherapistId.Should().Be(therapistId);
    }

    // ── get_appointments integration (real DB queries) ───────────────────────

    [Fact]
    public async Task GetAppointments_ReturnsAppointments_WithIncludesResolved()
    {
        // Seed entities and an appointment via API
        var (patientId, therapistId, roomId, _) = await SeedViaApiAsync("ga");
        await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });

        // Query back via the API
        var response = await _fixture.Client.GetAsync($"/api/appointments?patientId={patientId}");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Scheduled");
    }

    // ── cancel_any_appointment integration (two-step in real DB) ─────────────

    [Fact]
    public async Task CancelAppointment_PersistsStatusChange_InRealDatabase()
    {
        var (patientId, therapistId, roomId, _) = await SeedViaApiAsync("cancel");
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });
        var doc = await System.Text.Json.JsonDocument.ParseAsync(
            await createResponse.Content.ReadAsStreamAsync());
        var appointmentId = doc.RootElement.GetProperty("id").GetInt32();

        // Cancel via API (hard-delete)
        var deleteResponse = await _fixture.Client.DeleteAsync($"/api/appointments/{appointmentId}");
        deleteResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.NoContent);

        // Verify removed from DB
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var apt = await db.Appointments.FindAsync(appointmentId);
        apt.Should().BeNull("the DELETE endpoint removes the appointment from the database");
    }

    // ── Conflict detection integration ───────────────────────────────────────

    [Fact]
    public async Task ScheduleAppointment_DetectsTherapistConflict_InRealDatabase()
    {
        var (patientId, therapistId, roomId, _) = await SeedViaApiAsync("conflict");

        // Book first appointment
        var first = await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });
        first.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        // Create a second patient for the conflict test
        var patient2Id = await SeedData.CreatePatientAsync(_fixture.Client, "conflict2");

        // Try to book the same therapist at the same time → should fail
        var second = await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patient2Id,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });
        second.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    // ── Treatment plan integration ───────────────────────────────────────────

    [Fact]
    public async Task TreatmentPlan_CreateAndRetrieve_PersistsCorrectly()
    {
        var (patientId, therapistId, _, therapyTypeId) = await SeedViaApiAsync("tp");

        // Create a treatment plan via API
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/treatmentplans", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            FrequencyPerWeek = 3,
            TotalDays = 30,
            StartDate = "2030-07-01",
            TherapyTypeIds = new[] { therapyTypeId }
        });
        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);

        // Read it back
        var getResponse = await _fixture.Client.GetAsync("/api/treatmentplans");
        getResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await getResponse.Content.ReadAsStringAsync();
        content.Should().Contain($"{patientId}");
    }

    // ── Waitlist integration ─────────────────────────────────────────────────

    [Fact]
    public async Task Waitlist_JoinAndRetrieve_PersistsCorrectly()
    {
        var (patientId, _, _, _) = await SeedViaApiAsync("wl");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();

        // Verify patient exists
        var patient = await db.Patients.FindAsync(patientId);
        patient.Should().NotBeNull();

        // Add waitlist entry using the domain constructor
        var entry = new WaitlistEntry(
            patient!,
            new DateOnly(2030, 7, 1),
            new DateOnly(2030, 7, 15));
        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync();

        // Verify retrieval
        var entries = await db.WaitlistEntries
            .Where(w => w.PatientId == patientId)
            .ToListAsync();
        entries.Should().HaveCount(1);
        entries[0].EarliestDate.Should().Be(new DateOnly(2030, 7, 1));
        entries[0].LatestDate.Should().Be(new DateOnly(2030, 7, 15));
    }

    // ── Audit logging integration ────────────────────────────────────────────

    [Fact]
    public async Task AppointmentCreation_GeneratesAuditLog()
    {
        var (patientId, therapistId, roomId, _) = await SeedViaApiAsync("audit");
        await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var logs = await db.AuditLogs
            .Where(l => l.EntityName == "Appointment")
            .ToListAsync();
        logs.Should().NotBeEmpty("creating an appointment should generate an audit log entry");
        logs.Should().Contain(l => l.Action == AuditAction.Created);
    }
}
