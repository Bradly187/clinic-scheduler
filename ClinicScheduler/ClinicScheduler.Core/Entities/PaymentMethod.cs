namespace ClinicScheduler.Core.Entities;

/// <summary>
/// How a payment was collected.
/// </summary>
public enum PaymentMethod
{
    /// <summary>Credit or debit card (typically via Stripe).</summary>
    Card,

    /// <summary>Cash payment at the clinic.</summary>
    Cash,

    /// <summary>Payment remitted by an insurance payer.</summary>
    Insurance,

    /// <summary>Any other payment method (check, bank transfer, etc.).</summary>
    Other
}
