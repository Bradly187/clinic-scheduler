namespace ClinicScheduler.Core.Interfaces;

/// <summary>
/// Abstraction over the payment processing provider (Stripe).
/// Implementations follow the graceful no-op pattern: when unconfigured,
/// <see cref="IsConfigured"/> returns false and operations return null/no-op.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Whether the payment gateway is configured and operational.</summary>
    bool IsConfigured { get; }

    /// <summary>Creates a Stripe Customer for a patient. Returns the Stripe customer ID.</summary>
    Task<string?> CreateCustomerAsync(string email, string name, CancellationToken ct = default);

    /// <summary>
    /// Creates a PaymentIntent for the given amount (in cents). Returns the client secret
    /// for the frontend to confirm the payment, plus the PaymentIntent ID.
    /// </summary>
    Task<PaymentIntentResult?> CreatePaymentIntentAsync(long amountCents, string currency, string? customerStripeId = null, string? description = null, CancellationToken ct = default);

    /// <summary>Refunds a completed PaymentIntent. Returns the refund ID.</summary>
    Task<string?> RefundPaymentAsync(string paymentIntentId, long? amountCents = null, CancellationToken ct = default);

    /// <summary>Attaches a payment method to a customer for future use.</summary>
    Task<bool> AttachPaymentMethodAsync(string paymentMethodId, string customerStripeId, CancellationToken ct = default);

    /// <summary>Validates a webhook signature and returns the deserialized event payload, or null if invalid.</summary>
    string? ValidateWebhookSignature(string payload, string signatureHeader);
}

/// <summary>Result of creating a PaymentIntent.</summary>
public sealed record PaymentIntentResult(string PaymentIntentId, string ClientSecret, string Status);
