namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// Core scheduling workflow: looking up appointments (Info), changing the schedule (Scheduling),
/// and managing the waitlist (Waitlist).
/// </summary>
public sealed class SchedulingWorkflowPack : IWorkflowPack
{
    private readonly ClinicProfile _profile;

    public SchedulingWorkflowPack(ClinicProfile profile) => _profile = profile;

    public string Name => "scheduling";

    public IEnumerable<SpecialistAgent> GetSpecialists()
    {
        var clinic = _profile.ClinicDescriptor;
        return
        [
            new SpecialistAgent(
                "info_agent",
                "Looks up and reports appointment information (e.g. \"what appointments do I have?\"). Read-only — cannot change anything.",
                $"You are the Info specialist for a {clinic}. You retrieve and clearly present " +
                "appointment information using your tools. You never book, cancel, or modify anything — if the " +
                "user asks for a change, tell them you'll hand that to the Scheduling specialist.",
                ["get_my_appointments", "get_appointments"]),

            new SpecialistAgent(
                "scheduling_agent",
                "Books, cancels, or reschedules appointments — use for any request that changes the schedule.",
                $"You are the Scheduling specialist for a {clinic}. You book, cancel, and reschedule " +
                "appointments using your tools. Cancelling and rescheduling both use a two-step confirmation: preview " +
                "first, then call again with confirmed=true only after the user agrees. Look up appointments when you " +
                "need an ID. Present results clearly.",
                ["get_my_appointments", "get_appointments", "schedule_appointment", "reschedule_appointment", "cancel_my_appointment", "cancel_any_appointment"]),

            new SpecialistAgent(
                "waitlist_agent",
                "Manages the waitlist: add a patient to it for a date window, list their waitlist entries, or remove one. Use when no slot is available now or the user mentions waiting for an opening.",
                $"You are the Waitlist specialist for a {clinic}. You add patients to the waitlist for a " +
                "date window (the system books the first matching opening automatically), list their active waitlist " +
                "entries, and remove entries on request. Confirm the date window and any preferences before adding, and " +
                "confirm which entry to remove (show the list first if needed). Present results clearly.",
                ["join_waitlist", "get_my_waitlist", "leave_waitlist"]),
        ];
    }
}
