using System.Globalization;
using ClinicSchedulerV2.Data.Model;

namespace ClinicSchedulerV2.Web;

/// <summary>Display formats shared by the pages, fixed to US conventions whatever the server's culture.</summary>
public static class Formats
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal amount) => amount.ToString("C", Us);

    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", Us);
    public static string Date(DateTime date) => date.ToString("MMM d, yyyy", Us);
    public static string DayAndTime(DateTime when) => when.ToString("ddd MMM d 'at' h:mm tt", Us);

    /// <summary>A soft color per service, used on the calendar and its legend.</summary>
    public static string ServiceColor(int serviceTypeId) => (serviceTypeId % 6) switch
    {
        1 => "#cde8e6", // new-patient evaluation
        2 => "#dbe7f7", // follow-up
        3 => "#f9dfc4", // infusion
        4 => "#e4dcf4", // injection
        5 => "#dff0d8", // lab draw
        _ => "#eeeeee",
    };

    public static string Describe(this Provider provider) => provider.DisplayName;

    public static string PatientLabel(Patient patient) => $"{patient.LastName}, {patient.FirstName} · {patient.DateOfBirth:MM/dd/yyyy}";
}
