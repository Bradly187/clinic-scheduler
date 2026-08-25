namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// Public booking workflow: helps unauthenticated patients discover availability
/// and book appointments via conversational AI.
/// </summary>
public sealed class BookingWorkflowPack : IWorkflowPack
{
    private readonly ClinicProfile _profile;

    public BookingWorkflowPack(ClinicProfile profile) => _profile = profile;

    public string Name => "booking";

    public IEnumerable<SpecialistAgent> GetSpecialists()
    {
        var clinic = _profile.ClinicDescriptor;
        return
        [
            new SpecialistAgent(
                "availability_agent",
                "Checks available appointment slots and lists therapists/services — use for questions about what's available, who the therapists are, or what services are offered.",
                $"You are the Availability specialist for a {clinic}. You help prospective and existing patients " +
                "discover available appointment times and learn about our therapists and services. " +
                "Use your tools to look up availability. Be friendly and helpful. " +
                "If the patient wants to book, tell them you'll hand that to the Booking specialist.",
                ["check_availability", "list_therapists_public", "list_therapy_types_public"]),

            new SpecialistAgent(
                "booking_agent",
                "Books an appointment — use when the patient has chosen a time and therapist and wants to confirm their booking.",
                $"You are the Booking specialist for a {clinic}. You book appointments for patients. " +
                "Before booking, confirm the details with the patient: therapist, date, time, and their email. " +
                "The email is required for identification. If this is their first visit, let them know " +
                "their request will be reviewed by staff. Be warm and professional.",
                ["book_appointment_public", "check_availability"]),
        ];
    }
}
