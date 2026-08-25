using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Web;
using ClinicScheduler.Web.Services.Skills;
using ClinicScheduler.Web.Services.Skills.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

/// <summary>
/// Exercises a single <see cref="ISkill"/> directly (no executor, no LLM) — the unit-testability
/// the self-registering skill model is meant to give. Covers the patient self-cancel auth path
/// and the two-step confirmation gate, which the executor-level suite does not.
/// </summary>
public class CancelMyAppointmentSkillTests
{
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly Mock<IRepository<Patient>> _patientRepo = new();
    private readonly Mock<IRepository<Appointment>> _appointmentRepo = new();
    private readonly Mock<ClinicScheduler.Shared.Services.IAppointmentEventService> _events = new();
    private readonly CancelMyAppointmentSkill _skill;

    public CancelMyAppointmentSkillTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Clinic:TimeZoneLabel"] = "clinic time" })
            .Build();
        _skill = new CancelMyAppointmentSkill(
            _user.Object, _patientRepo.Object, _appointmentRepo.Object, _events.Object, new ClinicTimeFormatter(config));
    }

    private void SetupPatient(string email)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, email), new Claim(ClaimTypes.Role, RoleNames.Patient) }, "TestAuth");
        _user.Setup(s => s.Principal).Returns(new ClaimsPrincipal(identity));
    }

    private (Patient, Appointment) SeedOwnAppointment(string email)
    {
        var patient = new Patient("John", "Doe", email, new DateOnly(1990, 1, 1)) { Id = 1 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var apt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 100 };
        _patientRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Patient> { patient });
        _appointmentRepo.Setup(r => r.GetByIdAsync(100, It.IsAny<CancellationToken>())).ReturnsAsync(apt);
        return (patient, apt);
    }

    [Fact]
    public async Task Previews_WithoutCanceling_WhenNotConfirmed()
    {
        SetupPatient("patient@test.com");
        var (_, apt) = SeedOwnAppointment("patient@test.com");

        var result = await _skill.ExecuteAsync(new JsonObject { ["appointmentId"] = 100 });

        result.Should().Contain("CONFIRMATION REQUIRED");
        apt.Status.Should().Be(AppointmentStatus.Scheduled);
        _appointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancels_OwnAppointment_WhenConfirmed()
    {
        SetupPatient("patient@test.com");
        var (_, apt) = SeedOwnAppointment("patient@test.com");

        var result = await _skill.ExecuteAsync(new JsonObject { ["appointmentId"] = 100, ["confirmed"] = true });

        result.Should().Contain("Successfully canceled appointment 100");
        apt.Status.Should().Be(AppointmentStatus.Canceled);
        _appointmentRepo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.NotifyAppointmentsChanged(), Times.Once);
    }

    [Fact]
    public async Task Rejects_WhenAppointmentBelongsToAnotherPatient()
    {
        SetupPatient("patient@test.com");
        var other = new Patient("Other", "Person", "other@test.com", new DateOnly(1990, 1, 1)) { Id = 2 };
        var therapist = new Therapist("Jane", "Smith", "jane@test.com");
        var room = new Room("Room 1", 1, null!);
        var foreignApt = new Appointment(other, therapist, room, DateTime.UtcNow.AddDays(1), TimeSpan.FromHours(1)) { Id = 200 };

        var me = new Patient("John", "Doe", "patient@test.com", new DateOnly(1990, 1, 1)) { Id = 1 };
        _patientRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Patient> { me });
        _appointmentRepo.Setup(r => r.GetByIdAsync(200, It.IsAny<CancellationToken>())).ReturnsAsync(foreignApt);

        var result = await _skill.ExecuteAsync(new JsonObject { ["appointmentId"] = 200, ["confirmed"] = true });

        result.Should().Contain("not authorized");
        foreignApt.Status.Should().Be(AppointmentStatus.Scheduled);
    }
}
