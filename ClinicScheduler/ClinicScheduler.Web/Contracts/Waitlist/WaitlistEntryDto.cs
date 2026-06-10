using ClinicScheduler.Core.Entities;

namespace ClinicScheduler.Web.Contracts.Waitlist;

/// <summary>A waitlist entry returned from the API.</summary>
public sealed class WaitlistEntryDto
{
    /// <summary>The unique identifier of the entry.</summary>
    public int Id { get; init; }

    /// <summary>The waiting patient's ID.</summary>
    public int PatientId { get; init; }

    /// <summary>The waiting patient's full name.</summary>
    public string PatientName { get; init; } = string.Empty;

    /// <summary>Preferred therapist ID; null means any clinical therapist.</summary>
    public int? TherapistId { get; init; }

    /// <summary>Preferred therapist name, when one is set.</summary>
    public string? TherapistName { get; init; }

    /// <summary>Preferred location ID; null means any location.</summary>
    public int? LocationId { get; init; }

    /// <summary>Preferred location name, when one is set.</summary>
    public string? LocationName { get; init; }

    /// <summary>First date the patient can attend.</summary>
    public DateOnly EarliestDate { get; init; }

    /// <summary>Last date the patient can attend.</summary>
    public DateOnly LatestDate { get; init; }

    /// <summary>Earliest acceptable slot start time, if restricted.</summary>
    public TimeOnly? PreferredTimeFrom { get; init; }

    /// <summary>Latest acceptable slot start time, if restricted.</summary>
    public TimeOnly? PreferredTimeTo { get; init; }

    /// <summary>Current entry status.</summary>
    public WaitlistStatus Status { get; init; }

    /// <summary>Free-form staff notes.</summary>
    public string? Notes { get; init; }

    /// <summary>The appointment booked when the entry was fulfilled.</summary>
    public int? FulfilledAppointmentId { get; init; }

    /// <summary>When the entry was created (determines FIFO priority).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>When the entry was last updated.</summary>
    public DateTime UpdatedAt { get; init; }
}
