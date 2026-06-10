using System.ComponentModel.DataAnnotations;

namespace ClinicScheduler.Web.Contracts.TreatmentPlans;

/// <summary>
/// Request to generate the recurring appointment series for a treatment plan.
/// </summary>
public sealed class GenerateAppointmentsRequest
{
    /// <summary>
    /// The room to book sessions in. Its location determines operating hours and slot length.
    /// </summary>
    /// <example>1</example>
    [Required]
    [Range(1, int.MaxValue)]
    public int RoomId { get; init; }

    /// <summary>
    /// Time of day to aim for; each session books the nearest available slot.
    /// </summary>
    /// <example>09:00:00</example>
    public TimeOnly PreferredTime { get; init; } = new(9, 0);

    /// <summary>
    /// Days of week to book on (optional). Must contain exactly the plan's
    /// FrequencyPerWeek distinct days. Omit to use the default pattern
    /// (2x: Mon/Thu, 3x: Mon/Wed/Fri, 4x: Mon/Tue/Thu/Fri).
    /// </summary>
    public List<DayOfWeek>? PreferredDays { get; init; }
}
