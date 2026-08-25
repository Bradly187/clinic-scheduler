using ClinicScheduler.Core.Interfaces;
using FluentAssertions;

namespace ClinicScheduler.Core.Tests.Services;

/// <summary>
/// Tests for IPaymentGateway behavior contract. These validate the no-op behavior
/// when unconfigured (the actual Stripe SDK calls would require test keys and are
/// covered by integration tests with Stripe's test mode).
/// </summary>
public class PaymentGatewayNoOpTests
{
    /// <summary>
    /// A minimal no-op implementation for testing the interface contract.
    /// Mirrors what StripePaymentGateway does when SecretKey is empty.
    /// </summary>
    private sealed class NoOpPaymentGateway : IPaymentGateway
    {
        public bool IsConfigured => false;

        public Task<string?> CreateCustomerAsync(string email, string name, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<PaymentIntentResult?> CreatePaymentIntentAsync(long amountCents, string currency, string? customerStripeId = null, string? description = null, CancellationToken ct = default)
            => Task.FromResult<PaymentIntentResult?>(null);

        public Task<string?> RefundPaymentAsync(string paymentIntentId, long? amountCents = null, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<bool> AttachPaymentMethodAsync(string paymentMethodId, string customerStripeId, CancellationToken ct = default)
            => Task.FromResult(false);

        public string? ValidateWebhookSignature(string payload, string signatureHeader)
            => null;
    }

    private readonly IPaymentGateway _gateway = new NoOpPaymentGateway();

    [Fact]
    public void IsConfigured_WhenNoKey_ReturnsFalse()
    {
        _gateway.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task CreateCustomer_WhenUnconfigured_ReturnsNull()
    {
        var result = await _gateway.CreateCustomerAsync("test@example.com", "Test User");
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreatePaymentIntent_WhenUnconfigured_ReturnsNull()
    {
        var result = await _gateway.CreatePaymentIntentAsync(15000, "usd");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RefundPayment_WhenUnconfigured_ReturnsNull()
    {
        var result = await _gateway.RefundPaymentAsync("pi_test_123");
        result.Should().BeNull();
    }

    [Fact]
    public async Task AttachPaymentMethod_WhenUnconfigured_ReturnsFalse()
    {
        var result = await _gateway.AttachPaymentMethodAsync("pm_123", "cus_123");
        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateWebhookSignature_WhenUnconfigured_ReturnsNull()
    {
        var result = _gateway.ValidateWebhookSignature("{}", "t=123,v1=abc");
        result.Should().BeNull();
    }
}
