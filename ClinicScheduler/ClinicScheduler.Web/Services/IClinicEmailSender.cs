namespace ClinicScheduler.Web.Services;

/// <summary>Sends transactional email (appointment reminders, notifications).</summary>
public interface IClinicEmailSender
{
    /// <summary>True when an SMTP host and from-address are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends a single email. Throws on delivery failure; callers decide whether that is fatal.</summary>
    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default);
}
