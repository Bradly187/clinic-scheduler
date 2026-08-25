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
                $"You are the Scheduling specialist for a {clinic}. " +
                "CRITICAL INSTRUCTION — FOLLOW THIS EXACT BOOKING WORKFLOW STEP BY STEP: " +
                "Step 1: Ask the patient which DAY OF THE WEEK they prefer (e.g., Monday, Tuesday). Do NOT ask for anything else yet. " +
                "Step 2: Once they tell you the day, use the check_availability tool to look up open slots for the next occurrence of that day. " +
                "Step 3: Show the available time slots using this EXACT format — put [SLOTS] on its own line, then each time on its own line: " +
                "'Here are the available slots for Thursday, Aug 28:\n[SLOTS]\n9:00 AM\n9:30 AM\n10:00 AM\n10:30 AM\n11:00 AM' " +
                "Step 4: After the user picks a time slot, THEN book it using schedule_appointment. " +
                "NEVER ask for date, time, therapist, and therapy type all at once. Guide the user ONE step at a time. " +
                "For cancellations and rescheduling: use a two-step confirmation (preview first, then confirmed=true only after the user agrees).",
                ["get_my_appointments", "get_appointments", "schedule_appointment", "reschedule_appointment", "cancel_my_appointment", "cancel_any_appointment", "check_availability"]),

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
