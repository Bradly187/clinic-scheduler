using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Two-step move of an appointment to a new slot. Books the new slot first (fully validated)
/// and only then cancels the original — so the original survives if the new slot can't be booked.
/// </summary>
public sealed class RescheduleAppointmentSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Patient> _patientRepository;
    private readonly IRepository<Therapist> _therapistRepository;
    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly AppointmentSchedulingService _schedulingService;
    private readonly IAppointmentEventService _appointmentEventService;
    private readonly ClinicDbContext _dbContext;
    private readonly IClinicTimeFormatter _clock;

    public RescheduleAppointmentSkill(
        ICurrentUserService currentUserService,
        IRepository<Patient> patientRepository,
        IRepository<Therapist> therapistRepository,
        IRepository<Appointment> appointmentRepository,
        AppointmentSchedulingService schedulingService,
        IAppointmentEventService appointmentEventService,
        ClinicDbContext dbContext,
        IClinicTimeFormatter clock)
    {
        _currentUserService = currentUserService;
        _patientRepository = patientRepository;
        _therapistRepository = therapistRepository;
        _appointmentRepository = appointmentRepository;
        _schedulingService = schedulingService;
        _appointmentEventService = appointmentEventService;
        _dbContext = dbContext;
        _clock = clock;
    }

    public string Name => "reschedule_appointment";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "reschedule_appointment",
            ["description"] = "Reschedules an existing appointment to a new date/time (optionally with a different therapist). Two-step: call first without 'confirmed' to preview, then again with confirmed=true. It books the new slot first (fully validated) and only then cancels the original — so the original is kept if the new slot can't be booked.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["appointmentId"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "The ID of the existing appointment to move."
                    },
                    ["date"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "New appointment date in YYYY-MM-DD format."
                    },
                    ["startTime"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "New start time in HH:MM (24-hour) format."
                    },
                    ["therapistName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional different therapist (full or partial name). Omit to keep the same therapist."
                    },
                    ["confirmed"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Set true ONLY after the user has explicitly confirmed. Omit or false to preview first."
                    }
                },
                ["required"] = new JsonArray { "appointmentId", "date", "startTime" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var appointmentId = SkillArgs.ParseOptionalInt(arguments?["appointmentId"]) ?? 0;
        var date = arguments?["date"]?.GetValue<string>();
        var startTime = arguments?["startTime"]?.GetValue<string>();
        var therapistName = arguments?["therapistName"]?.GetValue<string>();
        var confirmed = SkillArgs.ParseBool(arguments?["confirmed"]);

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (string.IsNullOrWhiteSpace(date)) return "Error: date is required (YYYY-MM-DD).";
        if (string.IsNullOrWhiteSpace(startTime)) return "Error: startTime is required (HH:MM).";
        if (!DateOnly.TryParse(date, out var parsedDate))
            return $"Error: Could not parse date '{date}'. Use YYYY-MM-DD format.";
        if (!TimeOnly.TryParse(startTime, out var parsedTime))
            return $"Error: Could not parse startTime '{startTime}'. Use HH:MM (24-hour) format.";

        // Load the existing appointment (tracked) with the nav properties we may carry over.
        var existing = await _dbContext.Appointments
            .Include(a => a.Therapist)
            .Include(a => a.TherapyType)
            .FirstOrDefaultAsync(a => a.Id == appointmentId);
        if (existing == null) return $"Error: Appointment with ID {appointmentId} not found.";

        if (existing.Status is AppointmentStatus.Canceled or AppointmentStatus.Completed)
            return $"Error: Cannot reschedule an appointment that is {existing.Status}.";

        // Authorization: patients may only reschedule their own appointment.
        if (!user.IsStaffOrAbove())
        {
            if (user.Identity?.Name == null) return "Error: User is not authenticated.";
            var patients = await _patientRepository.FindAsync(p => p.Email == user.Identity.Name);
            var patient = patients.FirstOrDefault();
            if (patient == null) return "Error: Patient record not found for the current user.";
            if (existing.PatientId != patient.Id) return "Error: You are not authorized to reschedule this appointment.";
        }

        // Resolve the target therapist (default: keep the same one).
        var newTherapistId = existing.TherapistId;
        var therapistChange = "";
        if (!string.IsNullOrWhiteSpace(therapistName))
        {
            var therapists = await _therapistRepository.FindAsync(
                t => t.FirstName.Contains(therapistName) || t.LastName.Contains(therapistName));
            if (!therapists.Any()) return $"Error: No therapist found matching '{therapistName}'.";
            if (therapists.Count > 1)
                return $"Multiple therapists match '{therapistName}': {string.Join(", ", therapists.Select(t => $"{t.FirstName} {t.LastName}"))}. Please be more specific.";
            newTherapistId = therapists.First().Id;
            therapistChange = $" with {therapists.First().FirstName} {therapists.First().LastName}";
        }

        var newStart = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day,
            parsedTime.Hour, parsedTime.Minute, 0, DateTimeKind.Utc);

        // Code-enforced confirmation: this cancels the original, so never proceed without confirmed=true.
        if (!confirmed)
            return $"CONFIRMATION REQUIRED: You are about to reschedule appointment {appointmentId} from "
                 + $"{_clock.Format(existing.StartTime)} to {_clock.Format(newStart)}{therapistChange}. "
                 + $"This books the new slot and cancels the original. Show these details to the user and ask them to "
                 + $"confirm. Only if they agree, call reschedule_appointment again with the same details and confirmed=true.";

        // Book the new slot first (fully validated). If this fails, the original is left untouched.
        Appointment newAppointment;
        try
        {
            newAppointment = await _schedulingService.CreateAppointmentAsync(
                existing.PatientId, newTherapistId, existing.RoomId, newStart, therapyType: existing.TherapyType);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"Could not reschedule: {ex.Message} Your original appointment (ID {appointmentId}, "
                 + $"{_clock.Format(existing.StartTime)}) is unchanged.";
        }

        // New slot secured — now cancel the original.
        existing.Cancel();
        await _appointmentRepository.UpdateAsync(existing);
        _appointmentEventService.NotifyAppointmentsChanged();

        return $"Rescheduled. Appointment {appointmentId} ({_clock.Format(existing.StartTime)}) was canceled and "
             + $"replaced by new appointment {newAppointment.Id} on {_clock.Format(newAppointment.StartTime)}{therapistChange}.";
    }
}
