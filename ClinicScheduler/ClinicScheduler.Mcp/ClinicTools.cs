using System.ComponentModel;
using System.Globalization;
using System.Text;
using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Database;
using ClinicSchedulerV2.Data.Model;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace ClinicScheduler.Mcp;

/// <summary>
/// MCP tools over the ClinicSchedulerV2 database. They resolve names to rows and send writes to
/// PostgreSQL; the database decides what is allowed (clinic hours, the 30-minute grid, double
/// booking, room type and capacity, provider qualification and location) and its message is
/// returned to the agent as-is.
///
/// These are staff operations acting on any patient by name, so expose this server only to
/// trusted operators. The destructive cancel_appointment tool enforces confirmation in code.
/// </summary>
[McpServerToolType]
public static class ClinicTools
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private static string When(DateTime dt) => dt.ToString("ddd, MMM d, yyyy h:mm tt", Us) + " (clinic time)";

    [McpServerTool(Name = "list_providers")]
    [Description("Lists the clinic's providers with their credential, the services each is qualified for, and the locations where they work. Use it to choose a provider before scheduling.")]
    public static async Task<string> ListProviders(ClinicDb clinic, CancellationToken ct)
    {
        await using var db = await clinic.OpenAsync(ct);
        var providers = await db.Providers.AsNoTracking()
            .Include(p => p.Employee).ThenInclude(e => e.Locations)
            .Include(p => p.Services)
            .Where(p => p.Employee.IsActive)
            .OrderBy(p => p.Employee.LastName)
            .AsSplitQuery()
            .ToListAsync(ct);
        if (providers.Count == 0) return "No providers are configured.";

        var sb = new StringBuilder("Providers:\n");
        foreach (var p in providers)
            sb.AppendLine($"- {p.DisplayName} — works at {string.Join(", ", p.Employee.Locations.OrderBy(l => l.LocationId).Select(l => l.ShortName))}"
                        + $" — qualified for {string.Join(", ", p.Services.OrderBy(s => s.ServiceTypeId).Select(s => s.Name))}");
        return sb.ToString();
    }

    [McpServerTool(Name = "list_services")]
    [Description("Lists the services that can be booked, with their usual length, the kind of room they need, and the fee.")]
    public static async Task<string> ListServices(ClinicDb clinic, CancellationToken ct)
    {
        await using var db = await clinic.OpenAsync(ct);
        var services = await db.ServiceTypes.AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.ServiceTypeId).ToListAsync(ct);
        var sb = new StringBuilder("Services:\n");
        foreach (var s in services)
            sb.AppendLine($"- {s.Name}: {s.DefaultDurationMin} minutes, {s.RequiredRoomType.ToString().ToLowerInvariant()} room, {s.Fee.ToString("C", Us)}");
        sb.AppendLine("Locations: " + string.Join(", ", (await db.Locations.AsNoTracking().OrderBy(l => l.LocationId).ToListAsync(ct)).Select(l => l.ShortName)));
        return sb.ToString();
    }

    [McpServerTool(Name = "get_appointments")]
    [Description("Retrieves a patient's upcoming scheduled appointments by patient name. Present the returned list (including appointment IDs) to the user.")]
    public static async Task<string> GetAppointments(
        ClinicDb clinic,
        [Description("Full or partial name of the patient.")] string patientName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patientName)) return "Error: patientName is required.";

        await using var db = await clinic.OpenAsync(ct);
        var patients = await FindPatients(db, patientName, ct);
        if (patients.Count == 0) return $"Error: No patient found matching '{patientName}'.";
        var ids = patients.Select(p => p.PatientId).ToList();

        var today = await ClinicTodayAsync(db, ct);
        var appointments = await db.Appointments.AsNoTracking()
            .Include(a => a.Patient).Include(a => a.ServiceType)
            .Include(a => a.Provider).ThenInclude(p => p.Employee)
            .Include(a => a.Room).ThenInclude(r => r.Location)
            .Where(a => ids.Contains(a.PatientId) && a.Status == AppointmentStatus.Scheduled && a.StartTime >= today)
            .OrderBy(a => a.StartTime)
            .ToListAsync(ct);
        if (appointments.Count == 0) return $"No upcoming scheduled appointments for '{patientName}'.";

        var sb = new StringBuilder($"Upcoming scheduled appointments for '{patientName}':\n");
        foreach (var a in appointments)
            sb.AppendLine($"- ID {a.AppointmentId}: {a.Patient.FullName}, {a.ServiceType.Name}, {When(a.StartTime)} to {a.EndTime.ToString("h:mm tt", Us)}, "
                        + $"with {a.Provider.DisplayName}, {a.Room.RoomName} at {a.Room.Location.ShortName}");
        return sb.ToString();
    }

    [McpServerTool(Name = "schedule_appointment")]
    [Description("Books a visit for a patient at a location; the database picks the first suitable room and checks every scheduling rule (weekdays 8 AM-5 PM on the half hour, no double booking, room capacity, provider qualified and working at that location). Confirm the details with the user before calling. Times are clinic-local.")]
    public static async Task<string> ScheduleAppointment(
        ClinicDb clinic,
        SchedulingService scheduling,
        DatabaseCatalog catalog,
        [Description("Full or partial name of the patient.")] string patientName,
        [Description("Full or partial name of the provider.")] string providerName,
        [Description("Service name, e.g. 'Follow-up visit' or 'IV biologic infusion' (see list_services).")] string serviceName,
        [Description("Location name, e.g. 'Medical Center', 'Memorial City' or 'Clear Lake'.")] string locationName,
        [Description("Appointment date in YYYY-MM-DD format.")] string date,
        [Description("Start time in HH:MM (24-hour) format, on the hour or half hour.")] string startTime,
        [Description("Optional length in minutes; defaults to the service's usual length.")] int? durationMinutes = null,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day))
            return $"Error: Could not read date '{date}'. Use YYYY-MM-DD.";
        if (!TimeOnly.TryParse(startTime, CultureInfo.InvariantCulture, out var time))
            return $"Error: Could not read startTime '{startTime}'. Use HH:MM (24-hour).";

        Patient patient; Provider provider; ServiceType service; Location location;
        await using (var db = await clinic.OpenAsync(ct))
        {
            var patients = await FindPatients(db, patientName, ct);
            if (One(patients, patientName, "patient", p => p.FullName) is { } patientError) return patientError;
            patient = patients[0];

            var providers = await db.Providers.AsNoTracking().Include(p => p.Employee)
                .Where(p => p.Employee.IsActive && EF.Functions.ILike(p.Employee.FirstName + " " + p.Employee.LastName, Like(providerName)))
                .ToListAsync(ct);
            if (One(providers, providerName, "provider", p => p.DisplayName) is { } providerError) return providerError;
            provider = providers[0];

            var services = await db.ServiceTypes.AsNoTracking()
                .Where(s => s.IsActive && EF.Functions.ILike(s.Name, Like(serviceName))).ToListAsync(ct);
            if (One(services, serviceName, "service", s => s.Name) is { } serviceError) return serviceError;
            service = services[0];

            var locations = await db.Locations.AsNoTracking()
                .Where(l => EF.Functions.ILike(l.Name, Like(locationName))).ToListAsync(ct);
            if (One(locations, locationName, "location", l => l.ShortName) is { } locationError) return locationError;
            location = locations[0];
        }

        var start = day.ToDateTime(time);
        var end = start.AddMinutes(durationMinutes ?? service.DefaultDurationMin);
        var result = await scheduling.BookAtLocationAsync(
            patient.PatientId, provider.EmployeeId, service.ServiceTypeId, location.LocationId, start, end, "Booked through the MCP server");
        if (!result.Succeeded) return "Could not book: " + await ExplainAsync(result.Violation!, catalog);

        await using var after = await clinic.OpenAsync(ct);
        var booked = await after.Appointments.AsNoTracking().Include(a => a.Room)
            .SingleAsync(a => a.AppointmentId == result.Id, ct);
        return $"Booked. ID {booked.AppointmentId}: {patient.FullName}, {service.Name}, {When(booked.StartTime)} to {booked.EndTime.ToString("h:mm tt", Us)}, "
             + $"with {provider.DisplayName}, {booked.Room.RoomName} at {location.ShortName}.";
    }

    [McpServerTool(Name = "cancel_appointment")]
    [Description("Cancels an appointment by ID on the clinic's behalf. SAFETY: call first WITHOUT confirmed (or confirmed=false) to preview; the appointment is only canceled when called again with confirmed=true after the user agrees.")]
    public static async Task<string> CancelAppointment(
        ClinicDb clinic,
        SchedulingService scheduling,
        DatabaseCatalog catalog,
        [Description("The ID of the appointment to cancel.")] int appointmentId,
        [Description("Set true ONLY after the user has explicitly confirmed. Omit/false to preview first.")] bool confirmed = false,
        [Description("Why it is being canceled; recorded on the cancellation.")] string? reason = null,
        CancellationToken ct = default)
    {
        Appointment? appointment;
        await using (var db = await clinic.OpenAsync(ct))
        {
            appointment = await db.Appointments.AsNoTracking()
                .Include(a => a.Patient).Include(a => a.ServiceType)
                .SingleOrDefaultAsync(a => a.AppointmentId == appointmentId, ct);
        }
        if (appointment is null) return $"Error: Appointment {appointmentId} not found.";
        if (appointment.Status != AppointmentStatus.Scheduled)
            return $"Appointment {appointmentId} is {appointment.Status}; only a scheduled appointment can be canceled.";

        var what = $"{appointment.Patient.FullName}'s {appointment.ServiceType.Name.ToLowerInvariant()} on {When(appointment.StartTime)}";

        // Code-enforced confirmation: never cancel until the caller passes confirmed=true.
        if (!confirmed)
            return $"CONFIRMATION REQUIRED: You are about to cancel appointment {appointmentId}, {what}. Show these details to the user "
                 + $"and ask them to confirm. Only if they agree, call cancel_appointment again with appointmentId={appointmentId} and confirmed=true.";

        var result = await scheduling.ClinicCancelAsync(appointmentId,
            string.IsNullOrWhiteSpace(reason) ? "Canceled by the clinic" : reason.Trim());
        return result.Succeeded
            ? $"Canceled appointment {appointmentId}, {what}. The patient and provider have been notified."
            : "Could not cancel: " + await ExplainAsync(result.Violation!, catalog);
    }

    [McpServerTool(Name = "list_pending_requests")]
    [Description("Lists patient requests waiting for staff review: new appointment requests and cancellation requests, oldest first.")]
    public static async Task<string> ListPendingRequests(DatabaseCatalog catalog, CancellationToken ct)
    {
        var queue = await catalog.PendingQueueAsync();
        if (queue.Rows.Count == 0) return "No requests are waiting for review.";
        var sb = new StringBuilder("Waiting for review (oldest first):\n");
        foreach (var r in queue.Rows)
            sb.AppendLine($"- {r[0]}: {r[1]}, {r[2]}, for {r[3]}{(r[4].Length > 0 ? $" — \"{r[4]}\"" : "")} (submitted {r[5]})");
        return sb.ToString();
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static string Like(string text) => "%" + text.Trim().Replace("%", "").Replace("_", "") + "%";

    private static Task<List<Patient>> FindPatients(ClinicDbContext db, string name, CancellationToken ct) =>
        db.Patients.AsNoTracking()
            .Where(p => p.IsActive && EF.Functions.ILike(p.FirstName + " " + p.LastName, Like(name)))
            .OrderBy(p => p.LastName).ToListAsync(ct);

    /// <summary>An error message unless exactly one row matched.</summary>
    private static string? One<T>(List<T> matches, string query, string kind, Func<T, string> label) => matches.Count switch
    {
        0 => $"Error: No {kind} found matching '{query}'.",
        1 => null,
        _ => $"Several {kind}s match '{query}': {string.Join(", ", matches.Select(label))}. Please be more specific.",
    };

    private static async Task<DateTime> ClinicTodayAsync(ClinicDbContext db, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<DateTime>("SELECT clinic_now()::date::timestamp AS \"Value\"").SingleAsync(ct));

    /// <summary>The database's own words: a trigger's message, or the description stored on the constraint.</summary>
    private static async Task<string> ExplainAsync(DbRuleViolation violation, DatabaseCatalog catalog) =>
        violation.Kind == DbRuleKind.BusinessRule
            ? violation.Message
            : await catalog.DescribeAsync(violation.Constraint) ?? violation.Message;
}
