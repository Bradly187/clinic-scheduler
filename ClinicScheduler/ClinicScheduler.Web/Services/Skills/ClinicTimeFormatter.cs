using Microsoft.Extensions.Configuration;

namespace ClinicScheduler.Web.Services.Skills;

/// <summary>
/// Formats appointment times for display as clinic-local wall-clock time
/// (e.g. "Tue, Jun 23, 2026 2:00 PM (clinic time)").
/// </summary>
public interface IClinicTimeFormatter
{
    string Format(DateTime dt);
}

/// <inheritdoc />
/// <remarks>
/// Appointment times are stored and validated as clinic wall-clock values, so the time-zone
/// label is purely a presentation suffix — configured via <c>Clinic:TimeZoneLabel</c>.
/// </remarks>
public sealed class ClinicTimeFormatter : IClinicTimeFormatter
{
    private readonly string _label;

    public ClinicTimeFormatter(IConfiguration configuration)
        => _label = configuration["Clinic:TimeZoneLabel"] ?? "clinic time";

    public string Format(DateTime dt)
        => string.IsNullOrWhiteSpace(_label)
            ? dt.ToString("ddd, MMM d, yyyy h:mm tt")
            : $"{dt:ddd, MMM d, yyyy h:mm tt} ({_label})";
}
