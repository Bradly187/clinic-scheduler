namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents a patient's insurance policy used for claims and superbill generation.
/// </summary>
public class InsurancePolicy
{
    public int Id { get; set; }

    public int PatientId { get; private set; }
    public Patient Patient { get; private set; } = null!;

    /// <summary>Insurance company name (e.g., "Blue Cross Blue Shield").</summary>
    public string ProviderName { get; private set; } = string.Empty;

    /// <summary>Policy/plan number.</summary>
    public string PolicyNumber { get; private set; } = string.Empty;

    /// <summary>Group number, if applicable.</summary>
    public string? GroupNumber { get; private set; }

    /// <summary>Subscriber ID (may differ from policy number for dependents).</summary>
    public string? SubscriberId { get; private set; }

    /// <summary>Date the policy became effective.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>Date the policy terminates, if known.</summary>
    public DateOnly? TerminationDate { get; private set; }

    /// <summary>Whether this policy is currently active for billing.</summary>
    public bool IsActive { get; private set; } = true;

    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Private constructor for EF Core.</summary>
    private InsurancePolicy() { }

    public InsurancePolicy(Patient patient, string providerName, string policyNumber, DateOnly effectiveDate, string? groupNumber = null, string? subscriberId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyNumber);

        Patient = patient;
        PatientId = patient.Id;
        ProviderName = providerName;
        PolicyNumber = policyNumber;
        EffectiveDate = effectiveDate;
        GroupNumber = groupNumber;
        SubscriberId = subscriberId;
    }

    public void UpdateDetails(string providerName, string policyNumber, DateOnly effectiveDate, string? groupNumber, string? subscriberId, DateOnly? terminationDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyNumber);

        ProviderName = providerName;
        PolicyNumber = policyNumber;
        EffectiveDate = effectiveDate;
        GroupNumber = groupNumber;
        SubscriberId = subscriberId;
        TerminationDate = terminationDate;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        TerminationDate ??= DateOnly.FromDateTime(DateTime.UtcNow);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        TerminationDate = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNotes(string? notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }
}
