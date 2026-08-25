using ClinicScheduler.Web.Services.Workflows;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

/// <summary>
/// Locks in the Workflow Pack refactor: packs contribute specialists as data, and the clinic
/// framing comes from configuration rather than a hardcoded "pain-management" literal.
/// </summary>
public class WorkflowPackTests
{
    private static ClinicProfile Profile(string? specialty = null)
    {
        var dict = new Dictionary<string, string?>();
        if (specialty != null) dict["Clinic:Specialty"] = specialty;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new ClinicProfile(config);
    }

    [Fact]
    public void ClinicProfile_DefaultsToPainManagement_WhenUnset()
    {
        var profile = Profile();
        profile.Specialty.Should().Be("pain-management");
        profile.ClinicDescriptor.Should().Be("pain-management clinic");
    }

    [Fact]
    public void ClinicProfile_RespectsConfiguredSpecialty()
    {
        var profile = Profile("cardiology");
        profile.Specialty.Should().Be("cardiology");
        profile.ClinicDescriptor.Should().Be("cardiology clinic");
    }

    [Fact]
    public void SchedulingPack_ContributesInfoSchedulingWaitlist_WithExpectedSkills()
    {
        var specialists = new SchedulingWorkflowPack(Profile()).GetSpecialists().ToList();

        specialists.Select(s => s.Name).Should()
            .BeEquivalentTo("info_agent", "scheduling_agent", "waitlist_agent");

        var info = specialists.Single(s => s.Name == "info_agent");
        info.SkillNames.Should().BeEquivalentTo("get_my_appointments", "get_appointments");

        var waitlist = specialists.Single(s => s.Name == "waitlist_agent");
        waitlist.SkillNames.Should().BeEquivalentTo("join_waitlist", "get_my_waitlist", "leave_waitlist");
    }

    [Fact]
    public void Specialty_FlowsIntoSpecialistPrompts()
    {
        var specialists = new SchedulingWorkflowPack(Profile("cardiology")).GetSpecialists().ToList();

        specialists.Should().OnlyContain(s => s.SystemPrompt.Contains("cardiology clinic"));
        specialists.Should().NotContain(s => s.SystemPrompt.Contains("pain-management clinic"));
    }

    [Fact]
    public void TriagePack_IsAdvisoryOnly_NoSkills()
    {
        var triage = new TriageWorkflowPack(Profile()).GetSpecialists().Single();
        triage.Name.Should().Be("triage_agent");
        triage.SkillNames.Should().BeEmpty();
    }
}
