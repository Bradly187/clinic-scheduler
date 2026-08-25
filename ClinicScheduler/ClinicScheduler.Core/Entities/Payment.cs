namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents a payment transaction against an invoice.
/// </summary>
public class Payment
{
    public int Id { get; set; }

    public int InvoiceId { get; private set; }
    public Invoice Invoice { get; private set; } = null!;

    /// <summary>Amount of this payment.</summary>
    public decimal Amount { get; private set; }

    /// <summary>How the payment was collected.</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>Processing status of this payment.</summary>
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    /// <summary>Stripe PaymentIntent ID for card payments.</summary>
    public string? StripePaymentIntentId { get; private set; }

    /// <summary>When the payment was confirmed/completed.</summary>
    public DateTime? PaidAt { get; private set; }

    /// <summary>When the payment was refunded, if applicable.</summary>
    public DateTime? RefundedAt { get; private set; }

    /// <summary>Optional notes (e.g., check number, insurance ERA reference).</summary>
    public string? Notes { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Private constructor for EF Core.</summary>
    private Payment() { }

    public Payment(Invoice invoice, decimal amount, PaymentMethod method, string? stripePaymentIntentId = null)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");

        Invoice = invoice;
        InvoiceId = invoice.Id;
        Amount = amount;
        Method = method;
        StripePaymentIntentId = stripePaymentIntentId;
        Status = PaymentStatus.Pending;
    }

    /// <summary>Marks the payment as successfully completed.</summary>
    public void MarkCompleted()
    {
        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Cannot complete a payment with status {Status}.");
        Status = PaymentStatus.Completed;
        PaidAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the payment as failed.</summary>
    public void MarkFailed()
    {
        if (Status != PaymentStatus.Pending)
            throw new InvalidOperationException($"Cannot mark a payment as failed with status {Status}.");
        Status = PaymentStatus.Failed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the payment as refunded.</summary>
    public void Refund()
    {
        if (Status != PaymentStatus.Completed)
            throw new InvalidOperationException("Only completed payments can be refunded.");
        Status = PaymentStatus.Refunded;
        RefundedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNotes(string? notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }
}
