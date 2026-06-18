using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;

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
    private readonly ClinicScheduler.Shared.Services.IAppointmentEventService _appointmentEventService;

    public SkillExecutor(
        ICurrentUserService currentUserService,
        IRepository<Appointment> appointmentRepository,
        IRepository<Patient> patientRepository,
        ClinicScheduler.Shared.Services.IAppointmentEventService appointmentEventService)
    {
        _currentUserService = currentUserService;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _appointmentEventService = appointmentEventService;
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
                    ["description"] = "Cancels any appointment. Provide either appointmentId or patientName. Only Staff or Admins can use this tool.",
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
                        }
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
            _ => throw new ArgumentException($"Unknown skill: {skillName}")
        };
    }

    public async Task<string> ExecuteAsync(string skillName, JsonObject? arguments)
    {
        try
        {
            if (skillName == "get_my_appointments")
            {
                return await GetMyAppointments();
            }
            else if (skillName == "cancel_my_appointment")
            {
                var id = arguments?["appointmentId"]?.GetValue<int>() ?? 0;
                return await CancelMyAppointment(id);
            }
            else if (skillName == "cancel_any_appointment")
            {
                int? id = null;
                var idNode = arguments?["appointmentId"];
                if (idNode != null)
                {
                    if (idNode.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                    {
                        id = idNode.GetValue<int>();
                    }
                    else if (idNode.GetValueKind() == System.Text.Json.JsonValueKind.String && int.TryParse(idNode.GetValue<string>(), out int parsedId))
                    {
                        id = parsedId;
                    }
                }
                var name = arguments?["patientName"]?.GetValue<string>();
                return await CancelAnyAppointment(id, name);
            }
            else if (skillName == "get_appointments")
            {
                var name = arguments?["patientName"]?.GetValue<string>();
                return await GetAppointments(name);
            }
            return $"Error: Unknown skill {skillName}.";
        }
        catch (Exception ex)
        {
            return $"Error executing skill {skillName}: {ex.Message}";
        }
    }

    private async Task<string> GetMyAppointments()
    {
        var user = _currentUserService.Principal;
        if (user == null || user.Identity?.Name == null) return "Error: User is not authenticated.";

        var email = user.Identity.Name;
        var patients = await _patientRepository.FindAsync(p => p.Email == email);
        var patient = patients.FirstOrDefault();

        if (patient == null) return "Error: Patient record not found for the current user.";

        var appointments = await _appointmentRepository.FindAsync(a => 
            a.PatientId == patient.Id && 
            a.StartTime >= DateTime.UtcNow &&
            a.Status != AppointmentStatus.Canceled &&
            a.Status != AppointmentStatus.Missed);

        if (appointments.Count == 0) return "You have no upcoming appointments.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Upcoming Appointments:");
        foreach (var apt in appointments.OrderBy(a => a.StartTime))
            sb.AppendLine($"- ID: {apt.Id}, Start Time: {apt.StartTime}, Status: {apt.Status}");
        return sb.ToString();
    }

    private async Task<string> CancelMyAppointment(int appointmentId)
    {
        var user = _currentUserService.Principal;
        if (user == null || user.Identity?.Name == null) return "Error: User is not authenticated.";

        var email = user.Identity.Name;
        var patients = await _patientRepository.FindAsync(p => p.Email == email);
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

        var isStaffOrAbove = user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.ClinicManager) || user.IsInRole(RoleNames.Staff) || user.IsInRole(RoleNames.Therapist);
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
        else if (!string.IsNullOrWhiteSpace(patientName))
        {
            var allPatients = await _patientRepository.GetAllAsync();
            var patients = allPatients.Where(p => 
                (p.FirstName + " " + p.LastName).Contains(patientName, StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (!patients.Any()) return $"Error: No patient found matching '{patientName}'.";
            
            var patientIds = patients.Select(p => p.Id).ToList();
            var appointments = await _appointmentRepository.FindAsync(a => patientIds.Contains(a.PatientId) && a.Status == AppointmentStatus.Scheduled && a.StartTime >= DateTime.UtcNow);
            
            if (!appointments.Any()) return $"No scheduled appointments found for patient '{patientName}'.";
            
            if (appointments.Count == 1)
            {
                var aptId = appointments.First().Id;
                var apt = await _appointmentRepository.GetByIdAsync(aptId);
                if (apt == null) return $"Error: Appointment with ID {aptId} not found.";
                apt.Cancel();
                await _appointmentRepository.UpdateAsync(apt);
                _appointmentEventService.NotifyAppointmentsChanged();
                return $"Successfully canceled appointment {apt.Id} for patient {patientName}.";
            }
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Multiple scheduled appointments found for patient '{patientName}'. You MUST present these options to the user and ask them to specify the ID of the appointment they wish to cancel:");
            foreach (var apt in appointments.OrderBy(a => a.StartTime))
            {
                sb.AppendLine($"- ID: {apt.Id}, StartTime: {apt.StartTime}");
            }
            return sb.ToString();
        }

        return "Error: You must provide either an appointmentId or a patientName.";
    }

    private async Task<string> GetAppointments(string? patientName)
    {
        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        var isStaffOrAbove = user.IsInRole(RoleNames.Admin) || user.IsInRole(RoleNames.ClinicManager) || user.IsInRole(RoleNames.Staff) || user.IsInRole(RoleNames.Therapist);
        if (!isStaffOrAbove) return "Error: Unauthorized. Only Staff or Admins can retrieve patient appointments.";

        if (string.IsNullOrWhiteSpace(patientName)) return "Error: You must provide a patientName.";

        var allPatients = await _patientRepository.GetAllAsync();
        var patients = allPatients.Where(p => 
            (p.FirstName + " " + p.LastName).Contains(patientName, StringComparison.OrdinalIgnoreCase)).ToList();
        
        if (!patients.Any()) return $"Error: No patient found matching '{patientName}'.";
        
        var patientIds = patients.Select(p => p.Id).ToList();
        var appointments = await _appointmentRepository.FindAsync(a => patientIds.Contains(a.PatientId) && a.Status == AppointmentStatus.Scheduled && a.StartTime >= DateTime.UtcNow);
        
        if (!appointments.Any()) return $"No upcoming scheduled appointments found for patient '{patientName}'.";
        
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Upcoming scheduled appointments for '{patientName}':");
        foreach (var apt in appointments.OrderBy(a => a.StartTime))
        {
            sb.AppendLine($"- ID: {apt.Id}, StartTime: {apt.StartTime}");
        }
        return sb.ToString();
    }
}
