using System.ComponentModel;
using System.Text;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace ClinicScheduler.Mcp;

/// <summary>
/// MCP tools that expose the clinic's scheduling operations to any MCP-capable agent.
/// Each tool reuses the application's domain logic (<see cref="AppointmentSchedulingService"/>
/// and <see cref="ClinicDbContext"/>), so operating-hours, slot-alignment, conflict, and
/// capacity rules are enforced exactly as they are in the web app.
///
/// These are staff/admin-style operations (they act by patient name). The MCP server should
/// therefore be exposed only to trusted operators — the same way the web app gates
/// cancel_any_appointment behind Staff/Admin roles.
/// </summary>
[McpServerToolType]
public static class ClinicTools
{
    /// <summary>Formats an appointment time for display as clinic-local wall-clock time.</summary>
    private static string FormatClinicTime(DateTime dt) => $"{dt:ddd, MMM d, yyyy h:mm tt} (clinic time)";

    [McpServerTool(Name = "list_therapists")]
    [Description("Lists the clinic's therapists and their specialties. Use this to find the correct therapist name before scheduling.")]
    public static async Task<string> ListTherapists(ClinicDbContext db, CancellationToken ct)
    {
        var therapists = await db.Therapists.AsNoTracking().OrderBy(t => t.LastName).ToListAsync(ct);
        if (therapists.Count == 0) return "No therapists are configured in the system.";

        var sb = new StringBuilder("Therapists:\n");
        foreach (var t in therapists)
            sb.AppendLine($"- {t.FirstName} {t.LastName}{(string.IsNullOrWhiteSpace(t.Specialty) ? "" : $" — {t.Specialty}")}");
        return sb.ToString();
    }

    [McpServerTool(Name = "get_appointments")]
    [Description("Retrieves a patient's upcoming scheduled appointments by patient name. Present the returned list (including appointment IDs) to the user.")]
    public static async Task<string> GetAppointments(
        ClinicDbContext db,
        [Description("Full or partial name of the patient.")] string patientName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patientName)) return "Error: patientName is required.";

        var patientIds = await db.Patients.AsNoTracking()
            .Where(p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName))
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (patientIds.Count == 0) return $"Error: No patient found matching '{patientName}'.";

        var appointments = await db.Appointments.AsNoTracking()
            .Include(a => a.Therapist)
            .Include(a => a.Room)
            .Where(a => patientIds.Contains(a.PatientId)
                     && a.Status == AppointmentStatus.Scheduled
                     && a.StartTime >= DateTime.UtcNow)
            .OrderBy(a => a.StartTime)
            .ToListAsync(ct);

        if (appointments.Count == 0) return $"No upcoming scheduled appointments found for patient '{patientName}'.";

        var sb = new StringBuilder($"Upcoming scheduled appointments for '{patientName}':\n");
        foreach (var a in appointments)
        {
            var therapist = a.Therapist != null ? $"{a.Therapist.FirstName} {a.Therapist.LastName}" : "Unknown";
            sb.AppendLine($"- ID: {a.Id}, {FormatClinicTime(a.StartTime)} (ends {a.EndTime:h:mm tt}), Therapist: {therapist}, Room: {a.Room?.Name ?? "Unknown"}");
        }
        return sb.ToString();
    }

    [McpServerTool(Name = "schedule_appointment")]
    [Description("Books a new appointment for a patient. Times are clinic-local. Confirm the details with the user before calling. Enforces operating hours, slot rules, conflicts, and daily capacity.")]
    public static async Task<string> ScheduleAppointment(
        ClinicDbContext db,
        AppointmentSchedulingService scheduler,
        [Description("Full or partial name of the patient.")] string patientName,
        [Description("Full or partial name of the therapist.")] string therapistName,
        [Description("Appointment date in YYYY-MM-DD format.")] string date,
        [Description("Appointment start time in HH:MM (24-hour) format.")] string startTime,
        [Description("Optional therapy type name.")] string? therapyTypeName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(patientName)) return "Error: patientName is required.";
        if (string.IsNullOrWhiteSpace(therapistName)) return "Error: therapistName is required.";
        if (string.IsNullOrWhiteSpace(date)) return "Error: date is required (YYYY-MM-DD).";
        if (string.IsNullOrWhiteSpace(startTime)) return "Error: startTime is required (HH:MM).";

        var patients = await db.Patients.AsNoTracking()
            .Where(p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName)).ToListAsync(ct);
        if (patients.Count == 0) return $"Error: No patient found matching '{patientName}'.";
        if (patients.Count > 1)
            return $"Multiple patients match '{patientName}': {string.Join(", ", patients.Select(p => $"{p.FirstName} {p.LastName} (ID:{p.Id})"))}. Please be more specific.";
        var patient = patients[0];

        var therapists = await db.Therapists.AsNoTracking()
            .Where(t => t.FirstName.Contains(therapistName) || t.LastName.Contains(therapistName)).ToListAsync(ct);
        if (therapists.Count == 0) return $"Error: No therapist found matching '{therapistName}'.";
        if (therapists.Count > 1)
            return $"Multiple therapists match '{therapistName}': {string.Join(", ", therapists.Select(t => $"{t.FirstName} {t.LastName}"))}. Please be more specific.";
        var therapist = therapists[0];

        TherapyType? therapyType = null;
        if (!string.IsNullOrWhiteSpace(therapyTypeName))
        {
            therapyType = await db.TherapyTypes.AsNoTracking().FirstOrDefaultAsync(tt => tt.Name.Contains(therapyTypeName), ct);
            if (therapyType == null) return $"Error: No therapy type found matching '{therapyTypeName}'.";
        }

        if (!DateOnly.TryParse(date, out var parsedDate))
            return $"Error: Could not parse date '{date}'. Use YYYY-MM-DD format.";
        if (!TimeOnly.TryParse(startTime, out var parsedTime))
            return $"Error: Could not parse startTime '{startTime}'. Use HH:MM (24-hour) format.";

        // Clinic-local wall-clock; tagged UTC to satisfy the timestamptz column (no shifting).
        var startDateTime = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day,
            parsedTime.Hour, parsedTime.Minute, 0, DateTimeKind.Utc);

        var room = await db.Rooms.AsNoTracking().OrderBy(r => r.Id).FirstOrDefaultAsync(ct);
        if (room == null) return "Error: No rooms are available in the system.";

        try
        {
            var appt = await scheduler.CreateAppointmentAsync(
                patient.Id, therapist.Id, room.Id, startDateTime, ct, therapyType);

            return $"Appointment scheduled successfully! ID: {appt.Id}, {FormatClinicTime(appt.StartTime)}, " +
                   $"Therapist: {therapist.FirstName} {therapist.LastName}, Room: {room.Name}, " +
                   $"Patient: {patient.FirstName} {patient.LastName}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"Could not schedule appointment: {ex.Message}";
        }
    }

    [McpServerTool(Name = "cancel_appointment")]
    [Description("Cancels an appointment by ID. SAFETY: call first WITHOUT confirmed (or confirmed=false) to preview the cancellation; the appointment is only cancelled when called again with confirmed=true after the user agrees.")]
    public static async Task<string> CancelAppointment(
        ClinicDbContext db,
        [Description("The ID of the appointment to cancel.")] int appointmentId,
        [Description("Set true ONLY after the user has explicitly confirmed. Omit/false to preview first.")] bool confirmed,
        CancellationToken ct)
    {
        var appointment = await db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Therapist)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (appointment == null) return $"Error: Appointment with ID {appointmentId} not found.";

        var who = appointment.Patient != null ? $"{appointment.Patient.FirstName} {appointment.Patient.LastName}" : "the patient";

        // Code-enforced confirmation: never cancel until the caller passes confirmed=true.
        if (!confirmed)
            return $"CONFIRMATION REQUIRED: You are about to cancel appointment {appointmentId} for {who} on "
                 + $"{FormatClinicTime(appointment.StartTime)}. Show these details to the user and ask them to confirm. "
                 + $"Only if they agree, call cancel_appointment again with appointmentId={appointmentId} and confirmed=true.";

        appointment.Cancel();
        await db.SaveChangesAsync(ct);
        return $"Successfully canceled appointment {appointmentId} for {who} ({FormatClinicTime(appointment.StartTime)}).";
    }
}
