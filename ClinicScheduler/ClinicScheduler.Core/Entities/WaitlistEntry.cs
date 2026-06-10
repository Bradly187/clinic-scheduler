namespace ClinicScheduler.Core.Entities;

public enum WaitlistStatus
{
    Active,
    Fulfilled,
    Canceled,
    Expired
}

/// <summary>
/// A patient waiting for an appointment slot to open up. An entry describes the
/// window the patient can attend (date range, optional time-of-day range) and
/// optional therapist/location preferences. The waitlist processor books the first
/// available matching slot and marks the entry fulfilled.
/// </summary>
public class WaitlistEntry
{
    /// <summary>Longest allowed date window, to bound slot searches.</summary>
    public const int MaxWindowDays = 90;

    public int Id { get; set; }

    public int PatientId { get; private set; }
    public Patient Patient { get; private set; } = null!;

    /// <summary>Preferred therapist; null means any clinical therapist.</summary>
    public int? TherapistId { get; private set; }
    public Therapist? Therapist { get; private set; }

    /// <summary>Preferred location; null means any location.</summary>
    public int? LocationId { get; private set; }
    public Location? Location { get; private set; }

    /// <summary>First date the patient can attend.</summary>
    public DateOnly EarliestDate { get; private set; }

    /// <summary>Last date the patient can attend; the entry expires after this.</summary>
    public DateOnly LatestDate { get; private set; }

    /// <summary>Earliest acceptable slot start time; null means any time.</summary>
    public TimeOnly? PreferredTimeFrom { get; private set; }

    /// <summary>Latest acceptable slot start time; null means any time.</summary>
    public TimeOnly? PreferredTimeTo { get; private set; }

    public WaitlistStatus Status { get; private set; } = WaitlistStatus.Active;

    public string? Notes { get; set; }

    /// <summary>The appointment booked when this entry was fulfilled.</summary>
    public int? FulfilledAppointmentId { get; private set; }
    public Appointment? FulfilledAppointment { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Private constructor for EF Core.</summary>
    private WaitlistEntry() { }

    public WaitlistEntry(
        Patient patient,
        DateOnly earliestDate,
        DateOnly latestDate,
        Therapist? therapist = null,
        Location? location = null,
        TimeOnly? preferredTimeFrom = null,
        TimeOnly? preferredTimeTo = null,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(patient);

        if (latestDate < earliestDate)
            throw new ArgumentException("Latest date must be on or after the earliest date.", nameof(latestDate));

        if (latestDate.DayNumber - earliestDate.DayNumber > MaxWindowDays)
            throw new ArgumentException($"The date window cannot exceed {MaxWindowDays} days.", nameof(latestDate));

        if (preferredTimeFrom is { } from && preferredTimeTo is { } to && from >= to)
            throw new ArgumentException("Preferred time 'from' must be earlier than 'to'.", nameof(preferredTimeFrom));

        Patient = patient;
        PatientId = patient.Id;
        Therapist = therapist;
        TherapistId = therapist?.Id;
        Location = location;
        LocationId = location?.Id;
        EarliestDate = earliestDate;
        LatestDate = latestDate;
        PreferredTimeFrom = preferredTimeFrom;
        PreferredTimeTo = preferredTimeTo;
        Notes = notes;
        Status = WaitlistStatus.Active;
    }

    /// <summary>Returns true when a slot start time satisfies this entry's date/time window.</summary>
    public bool Matches(DateTime slotStart)
    {
        var date = DateOnly.FromDateTime(slotStart);
        if (date < EarliestDate || date > LatestDate) return false;

        var time = TimeOnly.FromDateTime(slotStart);
        if (PreferredTimeFrom is { } from && time < from) return false;
        if (PreferredTimeTo is { } to && time > to) return false;

        return true;
    }

    /// <summary>Marks the entry fulfilled by the given booked appointment.</summary>
    public void Fulfill(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        if (Status != WaitlistStatus.Active)
            throw new InvalidOperationException($"Cannot fulfill a waitlist entry that is {Status}.");

        FulfilledAppointment = appointment;
        FulfilledAppointmentId = appointment.Id;
        Status = WaitlistStatus.Fulfilled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Cancels an active entry (patient no longer wants the slot).</summary>
    public void Cancel()
    {
        if (Status != WaitlistStatus.Active)
            throw new InvalidOperationException($"Cannot cancel a waitlist entry that is {Status}.");

        Status = WaitlistStatus.Canceled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks an active entry expired (its date window has passed).</summary>
    public void Expire()
    {
        if (Status != WaitlistStatus.Active)
            throw new InvalidOperationException($"Cannot expire a waitlist entry that is {Status}.");

        Status = WaitlistStatus.Expired;
        UpdatedAt = DateTime.UtcNow;
    }
}
