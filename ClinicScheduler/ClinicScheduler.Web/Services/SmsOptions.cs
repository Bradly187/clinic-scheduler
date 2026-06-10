namespace ClinicScheduler.Web.Services;

/// <summary>
/// Twilio settings bound from the "Sms" configuration section. Leaving
/// <see cref="AccountSid"/> empty disables outbound SMS; reminders then go out
/// via in-app notification and email only.
/// </summary>
public sealed class SmsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Sms";

    /// <summary>Twilio account SID. Empty disables SMS delivery.</summary>
    public string AccountSid { get; set; } = "";

    /// <summary>Twilio auth token.</summary>
    public string AuthToken { get; set; } = "";

    /// <summary>E.164 sender number (e.g. +15551234567).</summary>
    public string FromNumber { get; set; } = "";
}
