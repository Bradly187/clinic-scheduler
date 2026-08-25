using ClinicScheduler.Core.Interfaces;
using Microsoft.Extensions.Options;
using Stripe;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Stripe payment gateway implementation. When unconfigured (SecretKey empty),
/// all operations are no-ops that return null — the application can operate
/// in cash/insurance-only mode without Stripe keys.
/// </summary>
public sealed class StripePaymentGateway : IPaymentGateway
{
    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentGateway> _logger;

    public StripePaymentGateway(IOptions<StripeOptions> options, ILogger<StripePaymentGateway> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (IsConfigured)
        {
            StripeConfiguration.ApiKey = _options.SecretKey;
        }
    }

    /// <inheritdoc/>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.SecretKey);

    /// <inheritdoc/>
    public async Task<string?> CreateCustomerAsync(string email, string name, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Stripe not configured; skipping customer creation for {Email}", email);
            return null;
        }

        var service = new CustomerService();
        var customer = await service.CreateAsync(new CustomerCreateOptions
        {
            Email = email,
            Name = name
        }, cancellationToken: ct);

        _logger.LogInformation("Created Stripe customer {CustomerId} for {Email}", customer.Id, email);
        return customer.Id;
    }

    /// <inheritdoc/>
    public async Task<PaymentIntentResult?> CreatePaymentIntentAsync(
        long amountCents, string currency, string? customerStripeId = null,
        string? description = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Stripe not configured; skipping PaymentIntent creation");
            return null;
        }

        var service = new PaymentIntentService();
        var options = new PaymentIntentCreateOptions
        {
            Amount = amountCents,
            Currency = currency,
            Description = description,
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true
            }
        };

        if (!string.IsNullOrWhiteSpace(customerStripeId))
            options.Customer = customerStripeId;

        var intent = await service.CreateAsync(options, cancellationToken: ct);

        _logger.LogInformation("Created PaymentIntent {IntentId} for {Amount} {Currency}",
            intent.Id, amountCents, currency);

        return new PaymentIntentResult(intent.Id, intent.ClientSecret, intent.Status);
    }

    /// <inheritdoc/>
    public async Task<string?> RefundPaymentAsync(string paymentIntentId, long? amountCents = null, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Stripe not configured; skipping refund for {PaymentIntentId}", paymentIntentId);
            return null;
        }

        var service = new RefundService();
        var options = new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId
        };

        if (amountCents.HasValue)
            options.Amount = amountCents.Value;

        var refund = await service.CreateAsync(options, cancellationToken: ct);

        _logger.LogInformation("Created refund {RefundId} for PaymentIntent {IntentId}", refund.Id, paymentIntentId);
        return refund.Id;
    }

    /// <inheritdoc/>
    public async Task<bool> AttachPaymentMethodAsync(string paymentMethodId, string customerStripeId, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Stripe not configured; skipping payment method attach");
            return false;
        }

        var service = new PaymentMethodService();
        await service.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions
        {
            Customer = customerStripeId
        }, cancellationToken: ct);

        _logger.LogInformation("Attached PaymentMethod {MethodId} to customer {CustomerId}", paymentMethodId, customerStripeId);
        return true;
    }

    /// <inheritdoc/>
    public string? ValidateWebhookSignature(string payload, string signatureHeader)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            _logger.LogWarning("Stripe webhook validation called but Stripe is not configured");
            return null;
        }

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret);
            return stripeEvent.Type;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Invalid Stripe webhook signature");
            return null;
        }
    }
}
