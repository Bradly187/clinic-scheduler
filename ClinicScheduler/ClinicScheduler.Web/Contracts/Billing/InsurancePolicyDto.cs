using System.ComponentModel.DataAnnotations;

namespace ClinicScheduler.Web.Contracts.Billing;

public sealed class InsurancePolicyDto
{
    public int Id { get; init; }
    public int PatientId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string PolicyNumber { get; init; } = string.Empty;
    public string? GroupNumber { get; init; }
    public string? SubscriberId { get; init; }
    public DateOnly EffectiveDate { get; init; }
    public DateOnly? TerminationDate { get; init; }
    public bool IsActive { get; init; }
    public string? Notes { get; init; }
}

public sealed class CreateInsurancePolicyRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int PatientId { get; init; }

    [Required]
    [StringLength(200)]
    public string ProviderName { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string PolicyNumber { get; init; } = string.Empty;

    [StringLength(50)]
    public string? GroupNumber { get; init; }

    [StringLength(100)]
    public string? SubscriberId { get; init; }

    [Required]
    public DateOnly EffectiveDate { get; init; }

    public DateOnly? TerminationDate { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }
}

public sealed class UpdateInsurancePolicyRequest
{
    [Required]
    [StringLength(200)]
    public string ProviderName { get; init; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string PolicyNumber { get; init; } = string.Empty;

    [StringLength(50)]
    public string? GroupNumber { get; init; }

    [StringLength(100)]
    public string? SubscriberId { get; init; }

    [Required]
    public DateOnly EffectiveDate { get; init; }

    public DateOnly? TerminationDate { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }
}
