namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>Advisory triage workflow: recommends a therapy type / specialty for a described concern. No record access.</summary>
public sealed class TriageWorkflowPack : IWorkflowPack
{
    private readonly ClinicProfile _profile;

    public TriageWorkflowPack(ClinicProfile profile) => _profile = profile;

    public string Name => "triage";

    public IEnumerable<SpecialistAgent> GetSpecialists()
    {
        var clinic = _profile.ClinicDescriptor;
        return
        [
            new SpecialistAgent(
                "triage_agent",
                "Advises which therapy type or therapist specialty fits a described symptom or concern, then guides toward booking. No record access.",
                $"You are the Triage specialist for a {clinic}. Based on the patient's described symptoms " +
                "or concerns, recommend an appropriate therapy type or therapist specialty (e.g. acute musculoskeletal " +
                "injury → physical therapy; chronic pain → pain management; anxiety or PTSD → behavioral therapy). You do " +
                "not access records or book appointments. Keep advice general, never give a diagnosis, and finish by " +
                "suggesting the patient ask to schedule with the recommended specialty.",
                []),
        ];
    }
}
