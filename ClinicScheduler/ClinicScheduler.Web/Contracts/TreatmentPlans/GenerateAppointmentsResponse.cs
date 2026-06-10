using ClinicScheduler.Web.Contracts.Appointments;

namespace ClinicScheduler.Web.Contracts.TreatmentPlans;

/// <summary>
/// Result of generating the appointment series for a treatment plan.
/// </summary>
public sealed class GenerateAppointmentsResponse
{
    /// <summary>Sessions the plan still needed when generation started.</summary>
    /// <example>20</example>
    public required int SessionsRequested { get; init; }

    /// <summary>Sessions successfully booked in this run.</summary>
    /// <example>20</example>
    public required int SessionsBooked { get; init; }

    /// <summary>Sessions that could not be placed (clinic full within the search horizon).</summary>
    /// <example>0</example>
    public required int SessionsUnbooked { get; init; }

    /// <summary>The appointments created, in chronological order.</summary>
    public required IReadOnlyList<AppointmentDto> Appointments { get; init; }
}
