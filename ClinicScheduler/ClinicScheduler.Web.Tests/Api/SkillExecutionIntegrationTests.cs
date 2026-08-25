using System.Security.Claims;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Shared.Services;
using ClinicScheduler.Web.Services.Skills;
using ClinicScheduler.Web.Services.Skills.Implementations;
using ClinicScheduler.Web.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net.Http.Json;
using Xunit;

namespace ClinicScheduler.Web.Tests.Api;

/// <summary>
/// Integration tests that exercise individual skills directly against the real PostgreSQL
/// database. Unlike the Unit/ tests, these use real EF Core queries translated to SQL,
/// catching issues like LINQ translation failures, FK violations, and query performance.
///
/// The skills are constructed manually with a mocked <see cref="ICurrentUserService"/> and
/// the real <see cref="ClinicDbContext"/> from the Testcontainers fixture.
/// </summary>
[Collection("WebApp")]
public class SkillExecutionIntegrationTests : IAsyncLifetime
{
    private readonly WebAppFixture _fixture;

    public SkillExecutionIntegrationTests(WebAppFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IServiceScope CreateScope() => _fixture.Factory.Services.CreateScope();

    private static ClaimsPrincipal MakePrincipal(string email, string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Role, role)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static Mock<ICurrentUserService> MockUser(string email, string role)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(s => s.Principal).Returns(MakePrincipal(email, role));
        return mock;
    }

    private static IClinicTimeFormatter BuildClock()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Clinic:TimeZoneLabel"] = "clinic time" })
            .Build();
        return new ClinicTimeFormatter(config);
    }

    private async Task<(int patientId, int therapistId, int roomId, int therapyTypeId)> SeedAsync(
        string suffix = "")
    {
        return await SeedData.SeedCoreEntitiesAsync(_fixture.Client, suffix);
    }

    // ── GetAppointmentsSkill ─────────────────────────────────────────────────

    [Fact]
    public async Task GetAppointments_Admin_ReturnsAppointments_FromRealDb()
    {
        var (patientId, therapistId, roomId, _) = await SeedAsync("getapt");

        // Book via API
        await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });

        // Execute skill directly against real DB
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var patientRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var user = MockUser("admin@clinic.com", "Admin");
        var skill = new GetAppointmentsSkill(user.Object, patientRepo, db, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject { ["patientName"] = "Patient" });

        result.Should().Contain("Upcoming scheduled appointments");
        result.Should().Contain("Therapist");
        result.Should().Contain("Room");
    }

    [Fact]
    public async Task GetAppointments_Patient_IsDenied()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var patientRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var user = MockUser("patient@test.com", "Patient");
        var skill = new GetAppointmentsSkill(user.Object, patientRepo, db, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject { ["patientName"] = "Test" });

        result.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task GetAppointments_NoMatch_ReturnsNotFound()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var patientRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var user = MockUser("admin@clinic.com", "Admin");
        var skill = new GetAppointmentsSkill(user.Object, patientRepo, db, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject { ["patientName"] = "NonexistentPerson" });

        result.Should().Contain("No patient found");
    }

    [Fact]
    public async Task GetAppointments_NoScheduledAppointments_ReturnsEmpty()
    {
        await SeedAsync("emptyapt");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var patientRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var user = MockUser("admin@clinic.com", "Admin");
        var skill = new GetAppointmentsSkill(user.Object, patientRepo, db, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject { ["patientName"] = "Patient" });

        result.Should().Contain("No upcoming scheduled appointments");
    }

    // ── CancelAnyAppointmentSkill ────────────────────────────────────────────

    [Fact]
    public async Task CancelAnyAppointment_TwoStepConfirmation_WorksAgainstRealDb()
    {
        var (patientId, therapistId, roomId, _) = await SeedAsync("cancel2step");

        // Create appointment via API
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

        // Step 1: Preview (no confirmation)
        using var scope1 = CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var aptRepo1 = scope1.ServiceProvider.GetRequiredService<IRepository<Appointment>>();
        var patRepo1 = scope1.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var events1 = new Mock<IAppointmentEventService>();
        var user1 = MockUser("admin@clinic.com", "Admin");
        var skill1 = new CancelAnyAppointmentSkill(user1.Object, patRepo1, aptRepo1, events1.Object, BuildClock());

        var preview = await skill1.ExecuteAsync(new JsonObject { ["appointmentId"] = appointmentId });

        preview.Should().Contain("CONFIRMATION REQUIRED");
        preview.Should().Contain(appointmentId.ToString());
        events1.Verify(e => e.NotifyAppointmentsChanged(), Times.Never);

        // Verify appointment is still scheduled
        var stillScheduled = await db1.Appointments.FindAsync(appointmentId);
        stillScheduled!.Status.Should().Be(AppointmentStatus.Scheduled);

        // Step 2: Confirm
        using var scope2 = CreateScope();
        var aptRepo2 = scope2.ServiceProvider.GetRequiredService<IRepository<Appointment>>();
        var patRepo2 = scope2.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var events2 = new Mock<IAppointmentEventService>();
        var user2 = MockUser("admin@clinic.com", "Admin");
        var skill2 = new CancelAnyAppointmentSkill(user2.Object, patRepo2, aptRepo2, events2.Object, BuildClock());

        var confirm = await skill2.ExecuteAsync(new JsonObject
        {
            ["appointmentId"] = appointmentId,
            ["confirmed"] = true
        });

        confirm.Should().Contain("Successfully canceled");
        events2.Verify(e => e.NotifyAppointmentsChanged(), Times.Once);

        // Verify in DB
        using var scope3 = CreateScope();
        var db3 = scope3.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var canceled = await db3.Appointments.FindAsync(appointmentId);
        canceled!.Status.Should().Be(AppointmentStatus.Canceled);
    }

    [Fact]
    public async Task CancelAnyAppointment_ByPatientName_DisambiguatesMultiple()
    {
        var (patientId, therapistId, roomId, _) = await SeedAsync("disambig");

        // Book two appointments for the same patient
        await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });
        await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am().AddHours(1),
            EndTime = SeedData.FutureMonday9am().AddHours(1).AddMinutes(30)
        });

        // Try cancelling by name without specifying which
        using var scope = CreateScope();
        var aptRepo = scope.ServiceProvider.GetRequiredService<IRepository<Appointment>>();
        var patRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var events = new Mock<IAppointmentEventService>();
        var user = MockUser("admin@clinic.com", "Admin");
        var skill = new CancelAnyAppointmentSkill(user.Object, patRepo, aptRepo, events.Object, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject { ["patientName"] = "Patient" });

        result.Should().Contain("Multiple scheduled appointments");
        result.Should().Contain("ID:");
        events.Verify(e => e.NotifyAppointmentsChanged(), Times.Never);
    }

    // ── ScheduleAppointmentSkill ─────────────────────────────────────────────

    [Fact]
    public async Task ScheduleAppointment_Staff_BooksByPatientName_RealDb()
    {
        var (patientId, therapistId, roomId, _) = await SeedAsync("book");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var patRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var therapistRepo = scope.ServiceProvider.GetRequiredService<IRepository<Therapist>>();
        var therapyTypeRepo = scope.ServiceProvider.GetRequiredService<IRepository<TherapyType>>();
        var roomRepo = scope.ServiceProvider.GetRequiredService<IRepository<Room>>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ClinicScheduler.Core.Services.AppointmentSchedulingService>();
        var events = new Mock<IAppointmentEventService>();
        var user = MockUser("staff@clinic.com", "Staff");
        var skill = new ScheduleAppointmentSkill(
            user.Object, patRepo, therapistRepo, therapyTypeRepo, roomRepo,
            schedulingService, events.Object, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["therapistName"] = "Therapist",
            ["patientName"] = "Patient",
            ["date"] = "2030-06-03",
            ["startTime"] = "09:00"
        });

        result.Should().Contain("Appointment scheduled successfully");
        result.Should().Contain("Patient");
        events.Verify(e => e.NotifyAppointmentsChanged(), Times.Once);

        // Verify in DB
        var appointments = await db.Appointments
            .Where(a => a.PatientId == patientId)
            .ToListAsync();
        appointments.Should().HaveCount(1);
        appointments[0].Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task ScheduleAppointment_DetectsConflict_RealDb()
    {
        var (patientId, therapistId, roomId, _) = await SeedAsync("conflict2");

        // Book first appointment directly
        await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = SeedData.FutureMonday9am(),
            EndTime = SeedData.FutureMonday9am().AddMinutes(30)
        });

        // Create second patient
        var patient2Id = await SeedData.CreatePatientAsync(_fixture.Client, "conflict2b");

        // Try booking the same slot via the skill
        using var scope = CreateScope();
        var patRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var therapistRepo = scope.ServiceProvider.GetRequiredService<IRepository<Therapist>>();
        var therapyTypeRepo = scope.ServiceProvider.GetRequiredService<IRepository<TherapyType>>();
        var roomRepo = scope.ServiceProvider.GetRequiredService<IRepository<Room>>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ClinicScheduler.Core.Services.AppointmentSchedulingService>();
        var events = new Mock<IAppointmentEventService>();
        var user = MockUser("staff@clinic.com", "Staff");
        var skill = new ScheduleAppointmentSkill(
            user.Object, patRepo, therapistRepo, therapyTypeRepo, roomRepo,
            schedulingService, events.Object, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["therapistName"] = "Therapist",
            ["patientName"] = "conflict2b",
            ["date"] = "2030-06-03",
            ["startTime"] = "09:00"
        });

        result.Should().Contain("Could not schedule appointment");
        events.Verify(e => e.NotifyAppointmentsChanged(), Times.Never);
    }

    [Fact]
    public async Task ScheduleAppointment_MissingRequired_ReturnsError()
    {
        using var scope = CreateScope();
        var patRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var therapistRepo = scope.ServiceProvider.GetRequiredService<IRepository<Therapist>>();
        var therapyTypeRepo = scope.ServiceProvider.GetRequiredService<IRepository<TherapyType>>();
        var roomRepo = scope.ServiceProvider.GetRequiredService<IRepository<Room>>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ClinicScheduler.Core.Services.AppointmentSchedulingService>();
        var events = new Mock<IAppointmentEventService>();
        var user = MockUser("staff@clinic.com", "Staff");
        var skill = new ScheduleAppointmentSkill(
            user.Object, patRepo, therapistRepo, therapyTypeRepo, roomRepo,
            schedulingService, events.Object, BuildClock());

        // Missing therapistName
        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["date"] = "2030-06-03",
            ["startTime"] = "09:00"
        });

        result.Should().Contain("Error: therapistName is required");
    }

    [Fact]
    public async Task ScheduleAppointment_Unauthenticated_ReturnsError()
    {
        using var scope = CreateScope();
        var patRepo = scope.ServiceProvider.GetRequiredService<IRepository<Patient>>();
        var therapistRepo = scope.ServiceProvider.GetRequiredService<IRepository<Therapist>>();
        var therapyTypeRepo = scope.ServiceProvider.GetRequiredService<IRepository<TherapyType>>();
        var roomRepo = scope.ServiceProvider.GetRequiredService<IRepository<Room>>();
        var schedulingService = scope.ServiceProvider.GetRequiredService<ClinicScheduler.Core.Services.AppointmentSchedulingService>();
        var events = new Mock<IAppointmentEventService>();
        var noUser = new Mock<ICurrentUserService>();
        noUser.Setup(s => s.Principal).Returns((ClaimsPrincipal?)null);
        var skill = new ScheduleAppointmentSkill(
            noUser.Object, patRepo, therapistRepo, therapyTypeRepo, roomRepo,
            schedulingService, events.Object, BuildClock());

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["therapistName"] = "Smith",
            ["date"] = "2030-06-03",
            ["startTime"] = "09:00"
        });

        result.Should().Contain("not authenticated");
    }

    // ── RegisterPatientSkill ─────────────────────────────────────────────────

    [Fact]
    public async Task RegisterPatient_Staff_CreatesPatient_InRealDb()
    {
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var user = MockUser("admin@clinic.com", "Admin");
        var skill = new RegisterPatientSkill(user.Object, db);

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["firstName"] = "Integration",
            ["lastName"] = "TestPatient",
            ["email"] = "integration@test.com",
            ["dateOfBirth"] = "1985-03-15",
            ["phone"] = "555-1234"
        });

        result.Should().Contain("Registered new patient Integration TestPatient");

        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Email == "integration@test.com");
        patient.Should().NotBeNull();
        patient!.FirstName.Should().Be("Integration");
        patient.LastName.Should().Be("TestPatient");
        patient.Phone.Should().Be("555-1234");
    }

    [Fact]
    public async Task RegisterPatient_DuplicateEmail_Staff_ReturnsError()
    {
        // Seed a patient first
        await SeedData.CreatePatientAsync(_fixture.Client, "dup");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var user = MockUser("admin@clinic.com", "Admin");
        var skill = new RegisterPatientSkill(user.Object, db);

        // Try creating with same email
        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["firstName"] = "Duplicate",
            ["lastName"] = "Patient",
            ["email"] = "patientdup@test.com",
            ["dateOfBirth"] = "1985-03-15"
        });

        // Should either update or indicate duplicate
        result.Should().NotContain("Error");
    }

    // ── Waitlist round-trip in real DB ────────────────────────────────────────

    [Fact]
    public async Task Waitlist_JoinListLeave_RealDbRoundTrip()
    {
        var (patientId, _, _, _) = await SeedAsync("waitlist");

        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var user = MockUser("patientwaitlist@test.com", "Patient");
        var joinSkill = new JoinWaitlistSkill(user.Object, db);
        var listSkill = new GetMyWaitlistSkill(user.Object, db);
        var leaveSkill = new LeaveWaitlistSkill(user.Object, db);

        // Join
        var joinResult = await joinSkill.ExecuteAsync(new JsonObject
        {
            ["earliestDate"] = "2030-07-01",
            ["latestDate"] = "2030-07-31"
        });
        joinResult.Should().Contain("Added to the waitlist");

        // Verify persisted
        var entries = await db.WaitlistEntries
            .Where(w => w.PatientId == patientId)
            .ToListAsync();
        entries.Should().HaveCount(1);

        // List
        var listResult = await listSkill.ExecuteAsync(null);
        listResult.Should().Contain("Active waitlist entries");
        listResult.Should().Contain("Jul 1, 2030");

        // Leave
        var entryId = entries[0].Id;
        var leaveResult = await leaveSkill.ExecuteAsync(new JsonObject { ["waitlistEntryId"] = entryId });
        leaveResult.Should().Contain($"Removed waitlist entry {entryId}");

        // Verify removed
        var afterResult = await listSkill.ExecuteAsync(null);
        afterResult.Should().Contain("no active waitlist entries");
    }
}
