using ClinicScheduler.Core.Entities;

namespace ClinicScheduler.Web.Contracts.Billing;

public sealed class InvoiceDto
{
    public int Id { get; init; }
    public int PatientId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public int? AppointmentId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public InvoiceStatus Status { get; init; }
    public DateTime IssuedAt { get; init; }
    public DateTime DueDate { get; init; }
    public decimal SubTotal { get; init; }
    public decimal TaxAmount { get; init; }
    public decimal Total { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal Balance { get; init; }
    public string? Notes { get; init; }
    public List<InvoiceLineItemDto> LineItems { get; init; } = [];
    public List<PaymentDto> Payments { get; init; } = [];
}

public sealed class InvoiceLineItemDto
{
    public int Id { get; init; }
    public string Description { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Amount { get; init; }
    public string? BillingCode { get; init; }
    public int? TherapyTypeId { get; init; }
    public string? TherapyTypeName { get; init; }
}

public sealed class PaymentDto
{
    public int Id { get; init; }
    public decimal Amount { get; init; }
    public PaymentMethod Method { get; init; }
    public PaymentStatus Status { get; init; }
    public string? StripePaymentIntentId { get; init; }
    public DateTime? PaidAt { get; init; }
    public DateTime? RefundedAt { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
}
