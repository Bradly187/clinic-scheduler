namespace ClinicScheduler.Web.Contracts.Locations;

/// <summary>
/// One operating window for a location on a given day of week. A location may have
/// multiple windows per day (e.g. morning and afternoon sessions).
/// </summary>
public sealed class OperatingHoursDto
{
    /// <summary>
    /// The day of week this window applies to.
    /// </summary>
    /// <example>Monday</example>
    public DayOfWeek DayOfWeek { get; init; }

    /// <summary>
    /// When the window opens.
    /// </summary>
    /// <example>08:00:00</example>
    public TimeOnly StartTime { get; init; }

    /// <summary>
    /// When the window closes. Must be later than <see cref="StartTime"/>.
    /// </summary>
    /// <example>17:00:00</example>
    public TimeOnly EndTime { get; init; }
}
