namespace ClinicScheduler.Web.Services;

/// <summary>
/// Stripe settings bound from the "Stripe" configuration section. Leaving
/// <see cref="SecretKey"/> empty disables payment processing; invoices can
/// still be created and paid via non-card methods (cash, insurance).
/// </summary>
public sealed class StripeOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stripe";

    /// <summary>Stripe secret API key (sk_test_... or sk_live_...).</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Stripe publishable key for client-side Elements (pk_test_... or pk_live_...).</summary>
    public string PublishableKey { get; set; } = "";

    /// <summary>Webhook signing secret for verifying Stripe event signatures (whsec_...).</summary>
    public string WebhookSecret { get; set; } = "";
}
