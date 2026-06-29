using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Books a new appointment (patient self-book; Staff/Admin book for a named patient).</summary>
public sealed class ScheduleAppointmentSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Patient> _patientRepository;
    private readonly IRepository<Therapist> _therapistRepository;
    private readonly IRepository<TherapyType> _therapyTypeRepository;
    private readonly IRepository<Room> _roomRepository;
    private readonly AppointmentSchedulingService _schedulingService;
    private readonly IAppointmentEventService _appointmentEventService;
    private readonly IClinicTimeFormatter _clock;

    public ScheduleAppointmentSkill(
        ICurrentUserService currentUserService,
        IRepository<Patient> patientRepository,
        IRepository<Therapist> therapistRepository,
        IRepository<TherapyType> therapyTypeRepository,
        IRepository<Room> roomRepository,
        AppointmentSchedulingService schedulingService,
        IAppointmentEventService appointmentEventService,
        IClinicTimeFormatter clock)
    {
        _currentUserService = currentUserService;
        _patientRepository = patientRepository;
        _therapistRepository = therapistRepository;
        _therapyTypeRepository = therapyTypeRepository;
        _roomRepository = roomRepository;
        _schedulingService = schedulingService;
        _appointmentEventService = appointmentEventService;
        _clock = clock;
    }

    public string Name => "schedule_appointment";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "schedule_appointment",
            ["description"] = "Schedules a new appointment. Patients schedule for themselves; Staff/Admin can specify a patientName to book for any patient.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["therapistName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the therapist."
                    },
                    ["therapyTypeName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Name of the therapy type (optional)."
                    },
                    ["date"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Appointment date in YYYY-MM-DD format."
                    },
                    ["startTime"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Appointment start time in HH:MM (24-hour) format."
                    },
                    ["patientName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the patient. Staff/Admin only — patients are booked automatically under their own account."
                    }
                },
                ["required"] = new JsonArray { "therapistName", "date", "startTime" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var therapistName = arguments?["therapistName"]?.GetValue<string>();
        var therapyTypeName = arguments?["therapyTypeName"]?.GetValue<string>();
        var date = arguments?["date"]?.GetValue<string>();
        var startTimeStr = arguments?["startTime"]?.GetValue<string>();
        var patientName = arguments?["patientName"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (string.IsNullOrWhiteSpace(therapistName)) return "Error: therapistName is required.";
        if (string.IsNullOrWhiteSpace(date)) return "Error: date is required (YYYY-MM-DD).";
        if (string.IsNullOrWhiteSpace(startTimeStr)) return "Error: startTime is required (HH:MM).";

        // Resolve patient
        Patient? patient;
        if (user.IsStaffOrAbove() && !string.IsNullOrWhiteSpace(patientName))
        {
            var matches = await _patientRepository.FindAsync(
                p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName));
            if (!matches.Any()) return $"Error: No patient found matching '{patientName}'.";
            if (matches.Count > 1)
            {
                var names = string.Join(", ", matches.Select(p => $"{p.FirstName} {p.LastName} (ID:{p.Id})"));
                return $"Multiple patients match '{patientName}': {names}. Please be more specific.";
            }
            patient = matches.First();
        }
        else
        {
            if (user.Identity?.Name == null) return "Error: User is not authenticated.";
            var patients = await _patientRepository.FindAsync(p => p.Email == user.Identity.Name);
            patient = patients.FirstOrDefault();
            if (patient == null) return "Error: Patient record not found for the current user.";
        }

        // Resolve therapist
        var therapists = await _therapistRepository.FindAsync(
            t => t.FirstName.Contains(therapistName) || t.LastName.Contains(therapistName));
        if (!therapists.Any()) return $"Error: No therapist found matching '{therapistName}'.";
        if (therapists.Count > 1)
        {
            var names = string.Join(", ", therapists.Select(t => $"{t.FirstName} {t.LastName}"));
            return $"Multiple therapists match '{therapistName}': {names}. Please be more specific.";
        }
        var therapist = therapists.First();

        // Resolve therapy type (optional)
        TherapyType? therapyType = null;
        if (!string.IsNullOrWhiteSpace(therapyTypeName))
        {
            var types = await _therapyTypeRepository.FindAsync(tt => tt.Name.Contains(therapyTypeName));
            if (!types.Any()) return $"Error: No therapy type found matching '{therapyTypeName}'.";
            therapyType = types.First();
        }

        // Parse date and time
        if (!DateOnly.TryParse(date, out var parsedDate))
            return $"Error: Could not parse date '{date}'. Use YYYY-MM-DD format.";
        if (!TimeOnly.TryParse(startTimeStr, out var parsedTime))
            return $"Error: Could not parse startTime '{startTimeStr}'. Use HH:MM format.";

        // Times are interpreted as the clinic's local wall-clock time. The scheduling service
        // validates operating hours against these clock components, and the timestamptz column
        // requires DateTimeKind.Utc — so we tag the wall-clock value as UTC without shifting it.
        // (No time-zone conversion is applied; "2pm" books a 2pm clinic-local slot.)
        var startDateTime = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day,
            parsedTime.Hour, parsedTime.Minute, 0, DateTimeKind.Utc);

        // Auto-pick first available room
        var rooms = await _roomRepository.GetAllAsync();
        var room = rooms.FirstOrDefault();
        if (room == null) return "Error: No rooms are available in the system.";

        try
        {
            var appointment = await _schedulingService.CreateAppointmentAsync(
                patient.Id, therapist.Id, room.Id, startDateTime, therapyType: therapyType);

            _appointmentEventService.NotifyAppointmentsChanged();

            var therapistFullName = $"{therapist.FirstName} {therapist.LastName}";
            return $"Appointment scheduled successfully! ID: {appointment.Id}, {_clock.Format(appointment.StartTime)}, " +
                   $"Therapist: {therapistFullName}, Room: {room.Name}, Patient: {patient.FirstName} {patient.LastName}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"Could not schedule appointment: {ex.Message}";
        }
    }
}
