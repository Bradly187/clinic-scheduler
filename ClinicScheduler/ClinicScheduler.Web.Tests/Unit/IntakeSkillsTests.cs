using System.Security.Claims;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web;
using ClinicScheduler.Web.Services.Skills.Implementations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

/// <summary>
/// Direct tests for the Sprint 4 intake skills (no executor, no LLM): registration auth +
/// create/update, demographics read-back, and opening an encounter.
/// </summary>
public class IntakeSkillsTests : IDisposable
{
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly ClinicDbContext _db;

    public IntakeSkillsTests()
    {
        var options = new DbContextOptionsBuilder<ClinicDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new ClinicDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(string email, string role)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, email), new Claim(ClaimTypes.Role, role) }, "TestAuth");
        _user.Setup(s => s.Principal).Returns(new ClaimsPrincipal(identity));
    }

    private async Task<Patient> SeedPatient(string first, string last, string email)
    {
        var p = new Patient(first, last, email, new DateOnly(1990, 1, 1));
        _db.Patients.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    // ---- register_patient ----

    [Fact]
    public async Task RegisterPatient_StaffCreatesNewPatient()
    {
        SetUser("admin@test.com", RoleNames.Admin);
        var skill = new RegisterPatientSkill(_user.Object, _db);

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["firstName"] = "New",
            ["lastName"] = "Patient",
            ["email"] = "new@test.com",
            ["dateOfBirth"] = "1985-05-05",
            ["phone"] = "555-9000"
        });

        result.Should().Contain("Registered new patient New Patient");
        (await _db.Patients.CountAsync(p => p.Email == "new@test.com")).Should().Be(1);
    }

    [Fact]
    public async Task RegisterPatient_PatientUpdatesOwnRecord()
    {
        await SeedPatient("John", "Doe", "patient@test.com");
        SetUser("patient@test.com", RoleNames.Patient);
        var skill = new RegisterPatientSkill(_user.Object, _db);

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["firstName"] = "Jonathan",
            ["lastName"] = "Doe",
            ["dateOfBirth"] = "1990-01-01",
            ["phone"] = "555-1111"
        });

        result.Should().Contain("Updated your patient record");
        var updated = await _db.Patients.FirstAsync(p => p.Email == "patient@test.com");
        updated.FirstName.Should().Be("Jonathan");
        updated.Phone.Should().Be("555-1111");
    }

    [Fact]
    public async Task RegisterPatient_PatientCannotCreate_WhenNoOwnRecord()
    {
        SetUser("ghost@test.com", RoleNames.Patient);
        var skill = new RegisterPatientSkill(_user.Object, _db);

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["firstName"] = "Ghost",
            ["lastName"] = "User",
            ["dateOfBirth"] = "1990-01-01"
        });

        result.Should().Contain("ask staff");
        (await _db.Patients.CountAsync()).Should().Be(0);
    }

    // ---- verify_patient_demographics ----

    [Fact]
    public async Task VerifyDemographics_PatientSeesOwnRecord()
    {
        await SeedPatient("John", "Doe", "patient@test.com");
        SetUser("patient@test.com", RoleNames.Patient);
        var skill = new VerifyPatientDemographicsSkill(_user.Object, _db);

        var result = await skill.ExecuteAsync(null);

        result.Should().Contain("John Doe");
        result.Should().Contain("patient@test.com");
        result.Should().Contain("SMS reminders consent: no");
    }

    // ---- start_encounter ----

    [Fact]
    public async Task StartEncounter_StaffOpensEncounter_AndSyncsFhir()
    {
        var patient = await SeedPatient("John", "Doe", "patient@test.com");
        SetUser("staff@test.com", RoleNames.Staff);
        var fhir = new Mock<IFhirSyncService>();
        fhir.Setup(f => f.SyncEncounterAsync(It.IsAny<Encounter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("enc-fhir-1");
        var skill = new StartEncounterSkill(_user.Object, _db, fhir.Object);

        var result = await skill.ExecuteAsync(new JsonObject
        {
            ["patientName"] = "John Doe",
            ["reason"] = "Initial consult"
        });

        result.Should().Contain("Opened intake encounter");
        result.Should().Contain("Initial consult");
        var encounter = await _db.Encounters.SingleAsync();
        encounter.PatientId.Should().Be(patient.Id);
        encounter.Status.Should().Be(EncounterStatus.Planned);
        fhir.Verify(f => f.SyncEncounterAsync(It.IsAny<Encounter>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartEncounter_RejectsPatientRole()
    {
        await SeedPatient("John", "Doe", "patient@test.com");
        SetUser("patient@test.com", RoleNames.Patient);
        var skill = new StartEncounterSkill(_user.Object, _db, Mock.Of<IFhirSyncService>());

        var result = await skill.ExecuteAsync(new JsonObject { ["patientName"] = "John Doe" });

        result.Should().Contain("Unauthorized");
        (await _db.Encounters.CountAsync()).Should().Be(0);
    }
}
