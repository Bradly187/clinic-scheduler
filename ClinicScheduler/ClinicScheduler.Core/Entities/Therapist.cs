namespace ClinicScheduler.Core.Entities;
using System.Text.RegularExpressions;

public partial class Therapist
{
    public int Id { get; set; }
    
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public string Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Specialty { get; set; }
    public string? NpiNumber { get; private set; }

    /// <summary>Remote ID of this therapist (FHIR Practitioner) in the external EHR/FHIR system.</summary>
    public string? FhirId { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TreatmentPlan> TreatmentPlans { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<TherapistShift> Shifts { get; set; } = [];

    /// <summary>
    /// Optional slot duration specific to this therapist. If null, falls back to the location's default.
    /// </summary>
    public int? SlotDurationMinutes { get; private set; }

    /// <summary>
    /// Private constructor for EF Core.
    /// </summary>
    private Therapist()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        Email = string.Empty;
    }

    public Therapist(string firstName, string lastName, string email, string? phone = null, string? specialty = null, string? npiNumber = null)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        Specialty = specialty;
        SetNpiNumber(npiNumber);
    }

    public void UpdateContactInfo(string email, string? phone)
    {
        Email = email;
        Phone = phone;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(string firstName, string lastName, string? specialty)
    {
        FirstName = firstName;
        LastName = lastName;
        Specialty = specialty;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNpiNumber(string? npiNumber)
    {
        if (npiNumber is not null && !NpiNumberRegex().IsMatch(npiNumber))
            throw new ArgumentException("NPI number must be exactly 10 digits.", nameof(npiNumber));
        NpiNumber = npiNumber;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSlotDuration(int? minutes)
    {
        if (minutes is < Location.MinSlotDurationMinutes or > Location.MaxSlotDurationMinutes)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes),
                $"Slot duration must be between {Location.MinSlotDurationMinutes} and {Location.MaxSlotDurationMinutes} minutes.");
        }
        SlotDurationMinutes = minutes;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records the remote FHIR Practitioner id after an external sync.</summary>
    public void SetFhirId(string? fhirId)
    {
        FhirId = fhirId;
        UpdatedAt = DateTime.UtcNow;
    }

    [GeneratedRegex(@"^\d{10}$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NpiNumberRegex();
}