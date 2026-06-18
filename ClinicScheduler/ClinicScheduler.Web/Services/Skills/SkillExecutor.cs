using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills;

public interface ISkillExecutor
{
    JsonObject GetToolSchema(string skillName);
    Task<string> ExecuteAsync(string skillName, JsonObject? arguments);
}

public class SkillExecutor : ISkillExecutor
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly IRepository<Patient> _patientRepository;
    private readonly IRepository<Therapist> _therapistRepository;
    private readonly IRepository<TherapyType> _therapyTypeRepository;
    private readonly IRepository<Room> _roomRepository;
    private readonly AppointmentSchedulingService _schedulingService;
    private readonly ClinicScheduler.Shared.Services.IAppointmentEventService _appointmentEventService;
    private readonly ClinicDbContext _dbContext;

    public SkillExecutor(
        ICurrentUserService currentUserService,
        IRepository<Appointment> appointmentRepository,
        IRepository<Patient> patientRepository,
        IRepository<Therapist> therapistRepository,
        IRepository<TherapyType> therapyTypeRepository,
        IRepository<Room> roomRepository,
        AppointmentSchedulingService schedulingService,
        ClinicScheduler.Shared.Services.IAppointmentEventService appointmentEventService,
        ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _therapistRepository = therapistRepository;
        _therapyTypeRepository = therapyTypeRepository;
        _roomRepository = roomRepository;
        _schedulingService = schedulingService;
        _appointmentEventService = appointmentEventService;
        _dbContext = dbContext;
    }

    public JsonObject GetToolSchema(string skillName)
    {
        return skillName switch
        {
            "get_my_appointments" => new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_my_appointments",
                    ["description"] = "Retrieves a list of upcoming appointments for the currently logged in patient.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject()
                    }
                }
            },
            "cancel_my_appointment" => new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "cancel_my_appointment",
                    ["description"] = "Cancels an appointment for the currently logged in patient given the appointment ID.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["appointmentId"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "The ID of the appointment to cancel."
                            }
                        },
                        ["required"] = new JsonArray { "appointmentId" }
                    }
                }
            },
            "cancel_any_appointment" => new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "cancel_any_appointment",
                    ["description"] = "Cancels any appointment. Provide either appointmentId or patientName (at least one required). Only Staff or Admins can use this tool.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["appointmentId"] = new JsonObject
                            {
                                ["type"] = "integer",
                                ["description"] = "The ID of the appointment to cancel."
                            },
                            ["patientName"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The name of the patient to cancel an appointment for."
                            }
                        },
                        ["required"] = new JsonArray()
                    }
                }
            },
            "get_appointments" => new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = "get_appointments",
                    ["description"] = "Retrieves a list of scheduled future appointments for any patient by name. Only Staff or Admins can use this tool.",
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["patientName"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["description"] = "The name of the patient to find appointments for."
                            }
                        },
                        ["required"] = new JsonArray { "patientName" }
                    }
                }
            },
            "schedule_appointment" => new JsonObject
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
            },
            _ => throw new ArgumentException($"Unknown skill: {skillName}")
        };
    }

    public async Task<string> ExecuteAsync(string skillName, JsonObject? arguments)
    {
        try
        {
            return skillName switch
            {
                "get_my_appointments" => await GetMyAppointments(),
                "cancel_my_appointment" => await CancelMyAppointment(arguments?["appointmentId"]?.GetValue<int>() ?? 0),
                "cancel_any_appointment" => await CancelAnyAppointment(ParseOptionalInt(arguments?["appointmentId"]), arguments?["patientName"]?.GetValue<string>()),
                "get_appointments" => await GetAppointments(arguments?["patientName"]?.GetValue<string>()),
                "schedule_appointment" => await ScheduleAppointment(
                    arguments?["therapistName"]?.GetValue<string>(),
                    arguments?["therapyTypeName"]?.GetValue<string>(),
                    arguments?["date"]?.GetValue<string>(),
                    arguments?["startTime"]?.GetValue<string>(),
                    arguments?["patientName"]?.GetValue<string>()),
                _ => $"Error: Unknown skill {skillName}."
            };
        }
        catch (Exception ex)
        {
            return $"Error executing skill {skillName}: {ex.Message}";
        }
    }

    private static int? ParseOptionalInt(JsonNode? node)
    {
        if (node == null) return null;
        return node.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? node.GetValue<int>()
            : int.TryParse(node.GetValue<string>(), out var n) ? n : null;
    }

    private async Task<string> GetMyAppointments()
    {
        var user = _currentUserService.Principal;
        if (user?.Identity?.Name == null) return "Error: User is not authenticated.";

        var patients = await _patientRepository.FindAsync(p => p.Email == user.Identity.Name);
        var patient = patients.FirstOrDefault();
        if (patient == null) return "Error: Patient record not found for the current user.";

        var appointments = await _dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.Therapist)
            .Include(a => a.Room)
            .Where(a => a.PatientId == patient.Id
                     && a.StartTime >= DateTime.UtcNow
                     && a.Status != AppointmentStatus.Canceled
                     && a.Status != AppointmentStatus.Missed)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        if (appointments.Count == 0) return "You have no upcoming appointments.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Upcoming Appointments:");
        foreach (var apt in appointments)
        {
            var therapistName = apt.Therapist != null ? $"{apt.Therapist.FirstName} {apt.Therapist.LastName}" : "Unknown";
            var roomName = apt.Room?.Name ?? "Unknown";
            sb.AppendLine($"- ID: {apt.Id}, Date: {apt.StartTime:yyyy-MM-dd HH:mm} UTC, End: {apt.EndTime:HH:mm}, Therapist: {therapistName}, Room: {roomName}, Status: {apt.Status}");
        }
        return sb.ToString();
    }

    private async Task<string> CancelMyAppointment(int appointmentId)
    {
        var user = _currentUserService.Principal;
        if (user?.Identity?.Name == null) return "Error: User is not authenticated.";

        var patients = await _patientRepository.FindAsync(p => p.Email == user.Identity.Name);
        var patient = patients.FirstOrDefault();
        if (patient == null) return "Error: Patient record not found for the current user.";

        var appointment = await _appointmentRepository.GetByIdAsync(appointmentId);
        if (appointment == null) return $"Error: Appointment with ID {appointmentId} not found.";
        if (appointment.PatientId != patient.Id) return "Error: You are not authorized to cancel this appointment.";

        appointment.Cancel();
        await _appointmentRepository.UpdateAsync(appointment);
        _appointmentEventService.NotifyAppointmentsChanged();
        return $"Successfully canceled appointment {appointmentId}.";
    }

    private async Task<string> CancelAnyAppointment(int? appointmentId, string? patientName)
    {
        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        var isStaffOrAbove = user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.ClinicManager)
                          || user.IsInRole(RoleNames.Staff) || user.IsInRole(RoleNames.Therapist);
        if (!isStaffOrAbove) return "Error: Unauthorized. Only Staff or Admins can cancel any appointment.";

        if (appointmentId.HasValue && appointmentId.Value > 0)
        {
            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId.Value);
            if (appointment == null) return $"Error: Appointment with ID {appointmentId} not found.";
            appointment.Cancel();
            await _appointmentRepository.UpdateAsync(appointment);
            _appointmentEventService.NotifyAppointmentsChanged();
            return $"Successfully canceled appointment {appointmentId}.";
        }

        if (!string.IsNullOrWhiteSpace(patientName))
        {
            var patients = await _patientRepository.FindAsync(
                p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName));

            if (!patients.Any()) return $"Error: No patient found matching '{patientName}'.";

            var patientIds = patients.Select(p => p.Id).ToList();
            var appointments = await _appointmentRepository.FindAsync(
                a => patientIds.Contains(a.PatientId)
                  && a.Status == AppointmentStatus.Scheduled
                  && a.StartTime >= DateTime.UtcNow);

            if (!appointments.Any()) return $"No scheduled appointments found for patient '{patientName}'.";

            if (appointments.Count == 1)
            {
                var apt = appointments.First();
                apt.Cancel();
                await _appointmentRepository.UpdateAsync(apt);
                _appointmentEventService.NotifyAppointmentsChanged();
                return $"Successfully canceled appointment {apt.Id} for patient {patientName}.";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Multiple scheduled appointments found for patient '{patientName}'. Specify the ID of the one to cancel:");
            foreach (var apt in appointments.OrderBy(a => a.StartTime))
                sb.AppendLine($"- ID: {apt.Id}, StartTime: {apt.StartTime:yyyy-MM-dd HH:mm} UTC");
            return sb.ToString();
        }

        return "Error: You must provide either an appointmentId or a patientName.";
    }

    private async Task<string> GetAppointments(string? patientName)
    {
        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        var isStaffOrAbove = user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.ClinicManager)
                          || user.IsInRole(RoleNames.Staff) || user.IsInRole(RoleNames.Therapist);
        if (!isStaffOrAbove) return "Error: Unauthorized. Only Staff or Admins can retrieve patient appointments.";
        if (string.IsNullOrWhiteSpace(patientName)) return "Error: You must provide a patientName.";

        var patients = await _patientRepository.FindAsync(
            p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName));

        if (!patients.Any()) return $"Error: No patient found matching '{patientName}'.";

        var patientIds = patients.Select(p => p.Id).ToList();

        var appointments = await _dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.Therapist)
            .Include(a => a.Room)
            .Where(a => patientIds.Contains(a.PatientId)
                     && a.Status == AppointmentStatus.Scheduled
                     && a.StartTime >= DateTime.UtcNow)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        if (!appointments.Any()) return $"No upcoming scheduled appointments found for patient '{patientName}'.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Upcoming scheduled appointments for '{patientName}':");
        foreach (var apt in appointments)
        {
            var therapistName = apt.Therapist != null ? $"{apt.Therapist.FirstName} {apt.Therapist.LastName}" : "Unknown";
            var roomName = apt.Room?.Name ?? "Unknown";
            sb.AppendLine($"- ID: {apt.Id}, Date: {apt.StartTime:yyyy-MM-dd HH:mm} UTC, End: {apt.EndTime:HH:mm}, Therapist: {therapistName}, Room: {roomName}");
        }
        return sb.ToString();
    }

    private async Task<string> ScheduleAppointment(
        string? therapistName, string? therapyTypeName, string? date, string? startTimeStr, string? patientName)
    {
        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (string.IsNullOrWhiteSpace(therapistName)) return "Error: therapistName is required.";
        if (string.IsNullOrWhiteSpace(date)) return "Error: date is required (YYYY-MM-DD).";
        if (string.IsNullOrWhiteSpace(startTimeStr)) return "Error: startTime is required (HH:MM).";

        // Resolve patient
        Patient? patient;
        var isStaffOrAbove = user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.ClinicManager)
                          || user.IsInRole(RoleNames.Staff) || user.IsInRole(RoleNames.Therapist);

        if (isStaffOrAbove && !string.IsNullOrWhiteSpace(patientName))
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
            return $"Appointment scheduled successfully! ID: {appointment.Id}, Date: {appointment.StartTime:yyyy-MM-dd HH:mm} UTC, " +
                   $"Therapist: {therapistFullName}, Room: {room.Name}, Patient: {patient.FirstName} {patient.LastName}.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"Could not schedule appointment: {ex.Message}";
        }
    }
}
