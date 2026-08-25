namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents the state of a payment transaction.
/// </summary>
public enum PaymentStatus
{
    /// <summary>Payment has been initiated but not yet confirmed.</summary>
    Pending,

    /// <summary>Payment was successfully processed.</summary>
    Completed,

    /// <summary>Payment processing failed.</summary>
    Failed,

    /// <summary>Payment was refunded to the patient.</summary>
    Refunded
}
