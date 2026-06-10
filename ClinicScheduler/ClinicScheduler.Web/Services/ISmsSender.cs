namespace ClinicScheduler.Web.Services;

/// <summary>Sends transactional SMS (appointment reminders) to consenting patients.</summary>
public interface ISmsSender
{
    /// <summary>True when an SMS provider is fully configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Sends a single SMS. Throws on delivery failure; callers decide whether that is fatal.</summary>
    Task SendAsync(string toPhone, string message, CancellationToken ct = default);
}
