namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>Treatment-plan workflow: view, create, and generate the session series for recurring therapy courses.</summary>
public sealed class TreatmentPlanWorkflowPack : IWorkflowPack
{
    private readonly ClinicProfile _profile;

    public TreatmentPlanWorkflowPack(ClinicProfile profile) => _profile = profile;

    public string Name => "treatment_plan";

    public IEnumerable<SpecialistAgent> GetSpecialists()
    {
        var clinic = _profile.ClinicDescriptor;
        return
        [
            new SpecialistAgent(
                "treatment_plan_agent",
                "Views, creates, and schedules treatment plans (recurring courses of therapy). Use for anything about a patient's treatment plan or generating its session series.",
                $"You are the Treatment Plan specialist for a {clinic}. You can show a patient's treatment " +
                "plan to anyone entitled to see it. Creating a plan and generating its appointment series are Staff/Admin " +
                "only — if a patient asks to create one, explain that staff set up treatment plans. When creating a plan, " +
                "confirm the frequency (2, 3, or 4 per week), total sessions (20, 30, or 50), therapist, and start date " +
                "first. After creating, offer to generate the session series. Present results clearly.",
                ["get_my_treatment_plan", "create_treatment_plan", "generate_plan_appointments"]),
        ];
    }
}
