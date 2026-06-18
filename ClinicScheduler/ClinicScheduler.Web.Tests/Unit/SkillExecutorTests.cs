using System.Security.Claims;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Web;
using ClinicScheduler.Web.Services.Skills;
using FluentAssertions;
using Moq;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

public class SkillExecutorTests
{
    private readonly Mock<ICurrentUserService> _mockUserService;
    private readonly Mock<IRepository<Appointment>> _mockAppointmentRepo;
    private readonly Mock<IRepository<Patient>> _mockPatientRepo;
    private readonly Mock<ClinicScheduler.Shared.Services.IAppointmentEventService> _mockAppointmentEventService;
    private readonly SkillExecutor _executor;

    public SkillExecutorTests()
    {
        _mockUserService = new Mock<ICurrentUserService>();
        _mockAppointmentRepo = new Mock<IRepository<Appointment>>();
        _mockPatientRepo = new Mock<IRepository<Patient>>();
        _mockAppointmentEventService = new Mock<ClinicScheduler.Shared.Services.IAppointmentEventService>();

        _executor = new SkillExecutor(
            _mockUserService.Object,
            _mockAppointmentRepo.Object,
            _mockPatientRepo.Object,
            _mockAppointmentEventService.Object);
    }

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
        // Act
        var schema = _executor.GetToolSchema("get_my_appointments");

        // Assert
        schema.Should().NotBeNull();
        schema["type"]?.GetValue<string>().Should().Be("function");
        schema["function"]?["name"]?.GetValue<string>().Should().Be("get_my_appointments");
    }

    [Fact]
    public void GetToolSchema_ThrowsArgumentException_ForUnknownSkill()
    {
        // Act & Assert
        Action action = () => _executor.GetToolSchema("unknown_skill");
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_GetMyAppointments_ReturnsError_WhenNotAuthenticated()
    {
        // Arrange
        _mockUserService.Setup(s => s.Principal).Returns((ClaimsPrincipal?)null);

        // Act
        var result = await _executor.ExecuteAsync("get_my_appointments", null);

        // Assert
        result.Should().Contain("Error: User is not authenticated.");
    }

    [Fact]
    public async Task ExecuteAsync_GetMyAppointments_ReturnsAppointments_WhenFound()
    {
        // Arrange
        SetupUser("patient@test.com");
        
        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };

        _mockPatientRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Patient, bool>>>()))
            .ReturnsAsync(new List<Patient> { patient });

        // Need dummy therapist and room for appointment
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };

        _mockAppointmentRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>()))
            .ReturnsAsync(new List<Appointment> { apt });

        // Act
        var result = await _executor.ExecuteAsync("get_my_appointments", null);

        // Assert
        result.Should().Contain("Upcoming Appointments:");
        result.Should().Contain("ID: 100");
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_RejectsPatientRole()
    {
        // Arrange
        SetupUser("patient@test.com", RoleNames.Patient);
        var args = new JsonObject { ["appointmentId"] = 100 };

        // Act
        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        // Assert
        result.Should().Contain("Unauthorized. Only Staff or Admins");
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_AllowsAdminRole()
    {
        // Arrange
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["appointmentId"] = 100 };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };

        _mockAppointmentRepo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(apt);

        // Act
        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        // Assert
        result.Should().Contain("Successfully canceled appointment 100");
        _mockAppointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
        apt.Status.Should().Be(AppointmentStatus.Canceled);
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_PatientName_CancelsDirectly_WhenSingleAppointmentFound()
    {
        // Arrange
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["patientName"] = "John Doe" };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };

        _mockPatientRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Patient> { patient });
        _mockAppointmentRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { apt });
        _mockAppointmentRepo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(apt);

        // Act
        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        // Assert
        result.Should().Contain("Successfully canceled appointment 100 for patient John Doe");
        _mockAppointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
        apt.Status.Should().Be(AppointmentStatus.Canceled);
    }

    [Fact]
    public async Task ExecuteAsync_CancelAnyAppointment_PatientName_ReturnsOptions_WhenMultipleAppointmentsFound()
    {
        // Arrange
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["patientName"] = "John Doe" };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt1 = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };
        var apt2 = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(2), TimeSpan.FromHours(1)) { Id = 200 };

        _mockPatientRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Patient> { patient });
        _mockAppointmentRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { apt1, apt2 });

        // Act
        var result = await _executor.ExecuteAsync("cancel_any_appointment", args);

        // Assert
        result.Should().Contain("Multiple scheduled appointments found for patient 'John Doe'");
        result.Should().Contain("ID: 100");
        result.Should().Contain("ID: 200");
        _mockAppointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_GetAppointments_RejectsPatientRole()
    {
        // Arrange
        SetupUser("patient@test.com", RoleNames.Patient);
        var args = new JsonObject { ["patientName"] = "John Doe" };

        // Act
        var result = await _executor.ExecuteAsync("get_appointments", args);

        // Assert
        result.Should().Contain("Unauthorized. Only Staff or Admins");
    }

    [Fact]
    public async Task ExecuteAsync_GetAppointments_ReturnsList_WhenAppointmentsFound()
    {
        // Arrange
        SetupUser("admin@test.com", RoleNames.Admin);
        var args = new JsonObject { ["patientName"] = "John Doe" };

        var patient = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };

        _mockPatientRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Patient> { patient });
        _mockAppointmentRepo.Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Appointment, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { apt });

        // Act
        var result = await _executor.ExecuteAsync("get_appointments", args);

        // Assert
        result.Should().Contain("Upcoming scheduled appointments for 'John Doe'");
        result.Should().Contain("ID: 100");
    }
}
