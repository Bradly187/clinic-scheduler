using ClinicScheduler.Core.Entities;
using FluentAssertions;

namespace ClinicScheduler.Core.Tests.Entities;

public class InsurancePolicyTests
{
    private Patient CreateTestPatient() =>
        new("John", "Doe", "john@example.com", new DateOnly(1980, 1, 1));

    [Fact]
    public void Constructor_ShouldCreateActivePolicy()
    {
        var patient = CreateTestPatient();

        var policy = new InsurancePolicy(patient, "Blue Cross", "BCB-123456", new DateOnly(2024, 1, 1), "GRP-001", "SUB-789");

        policy.Patient.Should().Be(patient);
        policy.ProviderName.Should().Be("Blue Cross");
        policy.PolicyNumber.Should().Be("BCB-123456");
        policy.GroupNumber.Should().Be("GRP-001");
        policy.SubscriberId.Should().Be("SUB-789");
        policy.EffectiveDate.Should().Be(new DateOnly(2024, 1, 1));
        policy.IsActive.Should().BeTrue();
        policy.TerminationDate.Should().BeNull();
    }

    [Fact]
    public void Constructor_NullProviderName_ShouldThrow()
    {
        var patient = CreateTestPatient();

        var act = () => new InsurancePolicy(patient, "", "BCB-123456", new DateOnly(2024, 1, 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_NullPolicyNumber_ShouldThrow()
    {
        var patient = CreateTestPatient();

        var act = () => new InsurancePolicy(patient, "Blue Cross", "", new DateOnly(2024, 1, 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivate_ShouldSetInactiveAndTerminationDate()
    {
        var patient = CreateTestPatient();
        var policy = new InsurancePolicy(patient, "Aetna", "AET-999", new DateOnly(2023, 6, 1));

        policy.Deactivate();

        policy.IsActive.Should().BeFalse();
        policy.TerminationDate.Should().NotBeNull();
    }

    [Fact]
    public void Activate_ShouldClearTerminationDate()
    {
        var patient = CreateTestPatient();
        var policy = new InsurancePolicy(patient, "Aetna", "AET-999", new DateOnly(2023, 6, 1));
        policy.Deactivate();

        policy.Activate();

        policy.IsActive.Should().BeTrue();
        policy.TerminationDate.Should().BeNull();
    }

    [Fact]
    public void UpdateDetails_ShouldChangeAllFields()
    {
        var patient = CreateTestPatient();
        var policy = new InsurancePolicy(patient, "Aetna", "AET-999", new DateOnly(2023, 6, 1));

        policy.UpdateDetails("United Health", "UHC-555", new DateOnly(2024, 1, 1), "GRP-NEW", "SUB-NEW", new DateOnly(2025, 12, 31));

        policy.ProviderName.Should().Be("United Health");
        policy.PolicyNumber.Should().Be("UHC-555");
        policy.GroupNumber.Should().Be("GRP-NEW");
        policy.SubscriberId.Should().Be("SUB-NEW");
        policy.EffectiveDate.Should().Be(new DateOnly(2024, 1, 1));
        policy.TerminationDate.Should().Be(new DateOnly(2025, 12, 31));
    }
}
