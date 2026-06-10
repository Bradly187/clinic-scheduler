namespace ClinicScheduler.Web.Services;

/// <summary>
/// SMTP settings bound from the "Email" configuration section. Works with any
/// STARTTLS-capable relay (e.g. AWS SES SMTP). Leaving <see cref="SmtpHost"/>
/// empty disables outbound email; reminders then remain in-app only.
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Email";

    /// <summary>SMTP server host name. Empty disables email delivery.</summary>
    public string SmtpHost { get; set; } = "";

    /// <summary>SMTP port; 587 (STARTTLS) by default.</summary>
    public int SmtpPort { get; set; } = 587;

    /// <summary>SMTP user name; empty for unauthenticated relays.</summary>
    public string SmtpUser { get; set; } = "";

    /// <summary>SMTP password.</summary>
    public string SmtpPassword { get; set; } = "";

    /// <summary>From address for outbound mail.</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Display name for outbound mail.</summary>
    public string FromName { get; set; } = "Clinic Scheduler";
}
