namespace ClinicScheduler.Core.Entities;

/// <summary>
/// A patient visit / intake session — the front of the patient journey. Maps to a FHIR R4
/// Encounter. Created during intake (see the intake workflow) and optionally linked to a
/// therapist and location.
/// </summary>
public class Encounter
{
    public int Id { get; set; }

    public int PatientId { get; private set; }
    public Patient Patient { get; private set; } = null!;

    public int? TherapistId { get; private set; }
    public Therapist? Therapist { get; private set; }

    public int? LocationId { get; private set; }
    public Location? Location { get; private set; }

    public EncounterStatus Status { get; private set; } = EncounterStatus.Planned;

    /// <summary>Free-text reason for the visit (chief complaint / intake note).</summary>
    public string? ReasonText { get; private set; }

    public DateTime PeriodStart { get; private set; }
    public DateTime? PeriodEnd { get; private set; }

    /// <summary>Remote ID of this encounter (FHIR Encounter) in the external EHR/FHIR system.</summary>
    public string? FhirId { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Private constructor for EF Core.</summary>
    private Encounter() { }

    public Encounter(Patient patient, DateTime periodStart, string? reasonText = null,
        Therapist? therapist = null, Location? location = null)
    {
        Patient = patient ?? throw new ArgumentNullException(nameof(patient));
        PatientId = patient.Id;
        PeriodStart = periodStart;
        ReasonText = reasonText;
        Therapist = therapist;
        TherapistId = therapist?.Id;
        Location = location;
        LocationId = location?.Id;
        Status = EncounterStatus.Planned;
    }

    /// <summary>Marks the encounter as underway (patient arrived).</summary>
    public void Start()
    {
        if (Status != EncounterStatus.Planned)
            throw new InvalidOperationException($"Only a planned encounter can be started; this one is {Status}.");
        Status = EncounterStatus.InProgress;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Completes the encounter and stamps its end time.</summary>
    public void Finish(DateTime? periodEnd = null)
    {
        if (Status is EncounterStatus.Finished or EncounterStatus.Cancelled)
            throw new InvalidOperationException($"Cannot finish an encounter that is {Status}.");
        Status = EncounterStatus.Finished;
        PeriodEnd = periodEnd ?? DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Cancels the encounter before completion.</summary>
    public void Cancel()
    {
        if (Status is EncounterStatus.Finished or EncounterStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel an encounter that is {Status}.");
        Status = EncounterStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Updates the chief-complaint / reason text.</summary>
    public void SetReason(string? reasonText)
    {
        ReasonText = reasonText;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records the remote FHIR Encounter id after an external sync.</summary>
    public void SetFhirId(string? fhirId)
    {
        FhirId = fhirId;
        UpdatedAt = DateTime.UtcNow;
    }
}
