using System.ComponentModel.DataAnnotations;

namespace ClinicScheduler.Web.Contracts.Billing;

public sealed class CreateInvoiceRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int PatientId { get; init; }

    [Range(1, int.MaxValue)]
    public int? AppointmentId { get; init; }

    /// <summary>Invoice due date. Defaults to 30 days from now if not specified.</summary>
    public DateTime? DueDate { get; init; }

    [StringLength(2000)]
    public string? Notes { get; init; }
}

public sealed class AddLineItemRequest
{
    [Required]
    [StringLength(500)]
    public string Description { get; init; } = string.Empty;

    [Required]
    [Range(1, 1000)]
    public int Quantity { get; init; } = 1;

    [Required]
    [Range(0, 100000)]
    public decimal UnitPrice { get; init; }

    [StringLength(20)]
    public string? BillingCode { get; init; }

    [Range(1, int.MaxValue)]
    public int? TherapyTypeId { get; init; }
}

public sealed class RecordPaymentRequest
{
    [Required]
    [Range(0.01, 1000000)]
    public decimal Amount { get; init; }

    [Required]
    public Core.Entities.PaymentMethod Method { get; init; }

    [StringLength(500)]
    public string? StripePaymentIntentId { get; init; }

    [StringLength(1000)]
    public string? Notes { get; init; }
}
