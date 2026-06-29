namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// Patient intake workflow: conversational registration / demographics verification and opening
/// a visit (encounter). This pack is the proof of the model — it is added with no change to the
/// orchestrator; the coordinator picks up its <c>intake_agent</c> automatically.
/// </summary>
public sealed class IntakeWorkflowPack : IWorkflowPack
{
    private readonly ClinicProfile _profile;

    public IntakeWorkflowPack(ClinicProfile profile) => _profile = profile;

    public string Name => "intake";

    public IEnumerable<SpecialistAgent> GetSpecialists()
    {
        var clinic = _profile.ClinicDescriptor;
        return
        [
            new SpecialistAgent(
                "intake_agent",
                "Handles patient intake: registering a new patient or updating demographics, confirming details on file, and opening a visit (encounter). Use for \"I'm a new patient\", \"update my info\", \"check me in\", or starting a visit.",
                $"You are the Intake specialist for a {clinic}. You warmly guide patients through registration: " +
                "collect their name, date of birth, and contact details conversationally, read the details back, and " +
                "confirm before saving with register_patient. Use verify_patient_demographics to show what's on file. " +
                "Opening a visit (start_encounter) is Staff/Admin only — if a patient asks to start their own visit, " +
                "explain that staff check them in. Never give clinical or diagnostic advice; for symptom questions, " +
                "suggest the user ask about triage or scheduling. Present results clearly.",
                ["register_patient", "verify_patient_demographics", "start_encounter"]),
        ];
    }
}
