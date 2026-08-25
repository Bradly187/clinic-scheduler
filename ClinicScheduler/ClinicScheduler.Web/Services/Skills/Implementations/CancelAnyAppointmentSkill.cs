using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Staff/Admin two-step cancel of any appointment by ID or by patient name.</summary>
public sealed class CancelAnyAppointmentSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Patient> _patientRepository;
    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly IAppointmentEventService _appointmentEventService;
    private readonly IClinicTimeFormatter _clock;

    public CancelAnyAppointmentSkill(
        ICurrentUserService currentUserService,
        IRepository<Patient> patientRepository,
        IRepository<Appointment> appointmentRepository,
        IAppointmentEventService appointmentEventService,
        IClinicTimeFormatter clock)
    {
        _currentUserService = currentUserService;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _appointmentEventService = appointmentEventService;
        _clock = clock;
    }

    public string Name => "cancel_any_appointment";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "cancel_any_appointment",
            ["description"] = "Cancels any appointment. Provide either appointmentId or patientName (at least one required). Only Staff or Admins can use this tool. This is a two-step tool: call it first without 'confirmed' to get a confirmation prompt, then again with confirmed=true once the user agrees.",
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
                    },
                    ["confirmed"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Set to true ONLY after the user has explicitly confirmed they want to cancel. Omit or set false to preview the cancellation first."
                    }
                }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var appointmentId = SkillArgs.ParseOptionalInt(arguments?["appointmentId"]);
        var patientName = arguments?["patientName"]?.GetValue<string>();
        var confirmed = SkillArgs.ParseBool(arguments?["confirmed"]);

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (!user.IsStaffOrAbove()) return "Error: Unauthorized. Only Staff or Admins can cancel any appointment.";

        if (appointmentId.HasValue && appointmentId.Value > 0)
        {
            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId.Value);
            if (appointment == null) return $"Error: Appointment with ID {appointmentId} not found.";

            // Code-enforced confirmation: never cancel until the caller passes confirmed=true.
            if (!confirmed)
                return $"CONFIRMATION REQUIRED: You are about to cancel appointment {appointmentId.Value} scheduled for "
                     + $"{_clock.Format(appointment.StartTime)}. Show these details to the user and ask them to confirm. "
                     + $"Only if they agree, call cancel_any_appointment again with appointmentId={appointmentId.Value} and confirmed=true.";

            appointment.Cancel();
            await _appointmentRepository.UpdateAsync(appointment);
            _appointmentEventService.NotifyAppointmentsChanged();
            return $"Successfully canceled appointment {appointmentId.Value} ({_clock.Format(appointment.StartTime)}).";
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

                // Code-enforced confirmation: never cancel until the caller passes confirmed=true.
                if (!confirmed)
                    return $"CONFIRMATION REQUIRED: '{patientName}' has one scheduled appointment — ID {apt.Id} on "
                         + $"{_clock.Format(apt.StartTime)}. Show these details to the user and ask them to confirm. "
                         + $"Only if they agree, call cancel_any_appointment again with appointmentId={apt.Id} and confirmed=true.";

                apt.Cancel();
                await _appointmentRepository.UpdateAsync(apt);
                _appointmentEventService.NotifyAppointmentsChanged();
                return $"Successfully canceled appointment {apt.Id} for patient {patientName} ({_clock.Format(apt.StartTime)}).";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Multiple scheduled appointments found for patient '{patientName}'. Specify the ID of the one to cancel:");
            foreach (var apt in appointments.OrderBy(a => a.StartTime))
                sb.AppendLine($"- ID: {apt.Id}, {_clock.Format(apt.StartTime)}");
            return sb.ToString();
        }

        return "Error: You must provide either an appointmentId or a patientName.";
    }
}
