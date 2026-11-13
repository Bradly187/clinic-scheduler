namespace ClinicSchedulerV2.Web;

/// <summary>
/// Choices offered by the booking forms. They are deliberately wider than what the clinic
/// allows (07:00 to 17:30, any weekday or weekend): the forms do not decide what is valid.
/// The database does, and the forms show its answer.
/// </summary>
public static class Slots
{
    public static readonly IReadOnlyList<TimeSpan> StartTimes =
        Enumerable.Range(0, 22).Select(i => TimeSpan.FromMinutes(7 * 60 + i * 30)).ToList();

    public static readonly IReadOnlyList<int> LengthsInMinutes = [30, 60, 90, 120, 150, 180, 210, 240];

    public static string Label(TimeSpan time) => DateTime.Today.Add(time).ToString("h:mm tt");

    public static string LengthLabel(int minutes) =>
        minutes < 60 ? $"{minutes} min" : minutes % 60 == 0 ? $"{minutes / 60} hr" : $"{minutes / 60} hr {minutes % 60} min";

    public static string Range(DateTime start, DateTime end) => $"{start:h:mm} – {end:h:mm tt}";
}
