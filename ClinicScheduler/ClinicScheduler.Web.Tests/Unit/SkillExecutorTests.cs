using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web;
using ClinicScheduler.Web.Services.Skills;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

public class SkillExecutorTests : IDisposable
{
    private readonly Mock<ICurrentUserService> _mockUserService;
    private readonly Mock<IRepository<Appointment>> _mockAppointmentRepo;
    private readonly Mock<IRepository<Patient>> _mockPatientRepo;
    private readonly Mock<IRepository<Therapist>> _mockTherapistRepo;
    private readonly Mock<IRepository<TherapyType>> _mockTherapyTypeRepo;
    private readonly Mock<IRepository<Room>> _mockRoomRepo;
    private readonly Mock<ClinicScheduler.Shared.Services.IAppointmentEventService> _mockAppointmentEventService;
    private readonly ClinicDbContext _dbContext;
    private readonly SkillExecutor _executor;

    public SkillExecutorTests()
    {
        _mockUserService = new Mock<ICurrentUserService>();
        _mockAppointmentRepo = new Mock<IRepository<Appointment>>();
        _mockPatientRepo = new Mock<IRepository<Patient>>();
        _mockTherapistRepo = new Mock<IRepository<Therapist>>();
        _mockTherapyTypeRepo = new Mock<IRepository<TherapyType>>();
        _mockRoomRepo = new Mock<IRepository<Room>>();
        _mockAppointmentEventService = new Mock<ClinicScheduler.Shared.Services.IAppointmentEventService>();

        var options = new DbContextOptionsBuilder<ClinicDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ClinicDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Clinic:TimeZoneLabel"] = "clinic time" })
            .Build();

        _executor = new SkillExecutor(
            _mockUserService.Object,
            _mockAppointmentRepo.Object,
            _mockPatientRepo.Object,
            _mockTherapistRepo.Object,
            _mockTherapyTypeRepo.Object,
            _mockRoomRepo.Object,
            null!,  // AppointmentSchedulingService not exercised in these tests
            null!,  // TreatmentPlanScheduleService not exercised in these tests
            _mockAppointmentEventService.Object,
            _dbContext,
            config);
    }

    public void Dispose() => _dbContext.Dispose();

    private void SetupUser(string email, string role = RoleNames.Patient)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        _mockUserService.Setup(s => s.Principal).Returns(principal);
    }

    [Fact]
    public void GetToolSchema_ReturnsCorrectSchema_ForKnownSkill()
    {
        var schema = _executor.GetToolSchema("get_my_appointments");

        schema.Should().NotBeNull();
        schema["type"]?.GetValue<string>().Should().Be("function");
        schema["function"]?["name"]?.GetValue<string>().Should().Be("get_my_appointments");
    }

    [Fact]
    public void GetToolSchema_ThrowsArgumentException_ForUnknownSkill()
    {
        Action action = () => _executor.GetToolSchema("unknown_skill");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_GetMyAppointments_ReturnsError_WhenNotAuthenticated()
    {
        _mockUserService.Setup(s => s.Principal).Returns((ClaimsPrincipal?)null);

        var result = await _executor.ExecuteAsync("get_my_appointments", null);

        result.Should().Contain("Error: User is not authenticated.");
    }

    [Fact]
    public async Task ExecuteAsync_GetMyAppointments_ReturnsAppointments_WhenFound()
    {
        SetupUser("patient@test.com");

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        _mockPatientRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Patient> { patient });

        var therapist = new Therapist("Jane", "Smith", "jane@test.com") { Id = 1 };
        var location = new Location("Main Clinic", "123 St");
        var room = new Room("Room 1", 1, location) { Id = 1 };

        // Seed the in-memory DbContext so the appointment query returns data
        _dbContext.Patients.Add(patient);
        _dbContext.Therapists.Add(therapist);
        _dbContext.Locations.Add(location);
        _dbContext.Rooms.Add(room);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };
        _dbContext.Appointments.Add(apt);
        await _dbContext.SaveChangesAsync();

        var result = await _executor.ExecuteAsync("get_my_appointments", null);

        result.Should().Contain("Upcoming Appointments:");
        result.Should().Contain("ID: 100");
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_RejectsPatientRole()
    {
        SetupUser("patient@test.com", RoleNames.Patient);
        var args = new JsonObject { ["appointmentId"] = 100 };

        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        result.Should().Contain("Unauthorized. Only Staff or Admins");
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_AllowsAdminRole()
    {
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["appointmentId"] = 100, ["confirmed"] = true };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };

        _mockAppointmentRepo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(apt);

        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        result.Should().Contain("Successfully canceled appointment 100");
        _mockAppointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
        apt.Status.Should().Be(AppointmentStatus.Canceled);
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_RequiresConfirmation_WhenNotConfirmed()
    {
        SetupUser("admin@test.com", RoleNames.Admin);
        // No "confirmed" flag — the tool must preview, not cancel.
        var args = new JsonObject { ["appointmentId"] = 100 };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };

        _mockAppointmentRepo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(apt);

        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        result.Should().Contain("CONFIRMATION REQUIRED");
        result.Should().Contain("100");
        _mockAppointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
        apt.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_PatientName_CancelsDirectly_WhenSingleAppointmentFound()
    {
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["patientName"] = "John Doe", ["confirmed"] = true };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };

        _mockPatientRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Patient> { patient });
        _mockAppointmentRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { apt });

        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        result.Should().Contain("Successfully canceled appointment 100 for patient John Doe");
        _mockAppointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
        apt.Status.Should().Be(AppointmentStatus.Canceled);
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_PatientName_ReturnsOptions_WhenMultipleAppointmentsFound()
    {
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["patientName"] = "John Doe" };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt1 = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };
        var apt2 = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(2), TimeSpan.FromHours(1)) { Id = 200 };

        _mockPatientRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Patient> { patient });
        _mockAppointmentRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Appointment, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { apt1, apt2 });

        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        result.Should().Contain("Multiple scheduled appointments found for patient 'John Doe'");
        result.Should().Contain("ID: 100");
        result.Should().Contain("ID: 200");
        _mockAppointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_GetAppointments_RejectsPatientRole()
    {
        SetupUser("patient@test.com", RoleNames.Patient);
        var args = new JsonObject { ["patientName"] = "John Doe" };

        var result = await _executor.ExecuteAsync("get_appointments", args);

        result.Should().Contain("Unauthorized. Only Staff or Admins");
    }

    [Fact]
    public async Task ExecuteAsync_GetAppointments_ReturnsList_WhenAppointmentsFound()
    {
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["patientName"] = "John Doe" };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        _mockPatientRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Patient> { patient });

        var therapist = new Therapist("Jane", "Smith", "jane@test.com") { Id = 1 };
        var location = new Location("Main Clinic", "123 St");
        var room = new Room("Room 1", 1, location) { Id = 1 };

        _dbContext.Patients.Add(patient);
        _dbContext.Therapists.Add(therapist);
        _dbContext.Locations.Add(location);
        _dbContext.Rooms.Add(room);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };
        _dbContext.Appointments.Add(apt);
        await _dbContext.SaveChangesAsync();

        var result = await _executor.ExecuteAsync("get_appointments", args);

        result.Should().Contain("Upcoming scheduled appointments for 'John Doe'");
        result.Should().Contain("ID: 100");
    }

    [Fact]
    public async Task ExecuteAsync_Waitlist_Join_List_Leave_RoundTrips()
    {
        SetupUser("patient@test.com");
        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();

        // Join the waitlist for a date window.
        var joinResult = await _executor.ExecuteAsync("join_waitlist", new JsonObject
        {
            ["earliestDate"] = "2026-07-01",
            ["latestDate"] = "2026-07-15"
        });
        joinResult.Should().Contain("Added to the waitlist");
        _dbContext.WaitlistEntries.Count().Should().Be(1);

        // List it.
        var listResult = await _executor.ExecuteAsync("get_my_waitlist", null);
        listResult.Should().Contain("Active waitlist entries");
        listResult.Should().Contain("Jul 1, 2026");

        // Leave it.
        var entryId = _dbContext.WaitlistEntries.First().Id;
        var leaveResult = await _executor.ExecuteAsync("leave_waitlist", new JsonObject { ["waitlistEntryId"] = entryId });
        leaveResult.Should().Contain($"Removed waitlist entry {entryId}");

        // No longer active.
        var afterResult = await _executor.ExecuteAsync("get_my_waitlist", null);
        afterResult.Should().Contain("no active waitlist entries");
    }

    [Fact]
    public async Task ExecuteAsync_RescheduleAppointment_RequiresConfirmation_AndDoesNotCancelOriginal()
    {
        SetupUser("admin@test.com", RoleNames.Admin);

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com") { Id = 1 };
        var room = new Room("Room 1", 1, null!) { Id = 1 };
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };
        _dbContext.Patients.Add(patient);
        _dbContext.Therapists.Add(therapist);
        _dbContext.Rooms.Add(room);
        _dbContext.Appointments.Add(apt);
        await _dbContext.SaveChangesAsync();

        // No "confirmed" → must preview and must NOT touch the scheduling service (which is null here)
        // nor cancel the original.
        var result = await _executor.ExecuteAsync("reschedule_appointment", new JsonObject
        {
            ["appointmentId"] = 100,
            ["date"] = "2026-08-01",
            ["startTime"] = "10:00"
        });

        result.Should().Contain("CONFIRMATION REQUIRED");
        var unchanged = await _dbContext.Appointments.FindAsync(100);
        unchanged!.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task ExecuteAsync_CreateTreatmentPlan_RejectsPatientRole()
    {
        SetupUser("patient@test.com", RoleNames.Patient);
        var args = new JsonObject
        {
            ["patientName"] = "John Doe",
            ["therapistName"] = "Jane Smith",
            ["frequencyPerWeek"] = 3,
            ["totalDays"] = 30,
            ["startDate"] = "2026-08-01"
        };

        var result = await _executor.ExecuteAsync("create_treatment_plan", args);

        result.Should().Contain("Unauthorized. Only Staff or Admins");
        _dbContext.TreatmentPlans.Count().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_CreateTreatmentPlan_CreatesPlan_AndGetReturnsIt()
    {
        SetupUser("admin@test.com", RoleNames.Admin);
        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com") { Id = 1 };
        _dbContext.Patients.Add(patient);
        _dbContext.Therapists.Add(therapist);
        await _dbContext.SaveChangesAsync();

        var createResult = await _executor.ExecuteAsync("create_treatment_plan", new JsonObject
        {
            ["patientName"] = "John Doe",
            ["therapistName"] = "Jane Smith",
            ["frequencyPerWeek"] = 3,
            ["totalDays"] = 30,
            ["startDate"] = "2026-08-01"
        });
        createResult.Should().Contain("Created treatment plan");
        _dbContext.TreatmentPlans.Count().Should().Be(1);

        // Admin can read it back by patient name.
        var getResult = await _executor.ExecuteAsync("get_my_treatment_plan", new JsonObject { ["patientName"] = "John Doe" });
        getResult.Should().Contain("Treatment plan for John Doe");
        getResult.Should().Contain("3x/week for 30 sessions");
    }
}
