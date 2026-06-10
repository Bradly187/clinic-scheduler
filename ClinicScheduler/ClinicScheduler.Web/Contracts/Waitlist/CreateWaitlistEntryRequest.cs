using System.ComponentModel.DataAnnotations;

namespace ClinicScheduler.Web.Contracts.Waitlist;

/// <summary>Request to add a patient to the waitlist.</summary>
public sealed class CreateWaitlistEntryRequest
{
    /// <summary>The patient to add.</summary>
    /// <example>1</example>
    [Required]
    [Range(1, int.MaxValue)]
    public int PatientId { get; init; }

    /// <summary>Preferred therapist (optional; omit for any clinical therapist).</summary>
    public int? TherapistId { get; init; }

    /// <summary>Preferred location (optional; omit for any location).</summary>
    public int? LocationId { get; init; }

    /// <summary>First date the patient can attend.</summary>
    /// <example>2026-06-15</example>
    [Required]
    public DateOnly EarliestDate { get; init; }

    /// <summary>Last date the patient can attend (window max 90 days).</summary>
    /// <example>2026-07-15</example>
    [Required]
    public DateOnly LatestDate { get; init; }

    /// <summary>Earliest acceptable slot start time (optional).</summary>
    /// <example>14:00:00</example>
    public TimeOnly? PreferredTimeFrom { get; init; }

    /// <summary>Latest acceptable slot start time (optional).</summary>
    /// <example>17:00:00</example>
    public TimeOnly? PreferredTimeTo { get; init; }

    /// <summary>Free-form staff notes (optional).</summary>
    [StringLength(500)]
    public string? Notes { get; init; }
}
