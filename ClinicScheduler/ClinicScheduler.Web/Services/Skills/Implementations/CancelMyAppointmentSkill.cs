using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Two-step (preview → confirm) cancel of the current patient's own appointment.</summary>
public sealed class CancelMyAppointmentSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Patient> _patientRepository;
    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly IAppointmentEventService _appointmentEventService;
    private readonly IClinicTimeFormatter _clock;

    public CancelMyAppointmentSkill(
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

    public string Name => "cancel_my_appointment";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "cancel_my_appointment",
            ["description"] = "Cancels an appointment for the currently logged in patient given the appointment ID. This is a two-step tool: call it first without 'confirmed' to get a confirmation prompt, then again with confirmed=true once the user agrees.",
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
                    ["confirmed"] = new JsonObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Set to true ONLY after the user has explicitly confirmed they want to cancel. Omit or set false to preview the cancellation first."
                    }
                },
                ["required"] = new JsonArray { "appointmentId" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var appointmentId = SkillArgs.ParseOptionalInt(arguments?["appointmentId"]) ?? 0;
        var confirmed = SkillArgs.ParseBool(arguments?["confirmed"]);

        var user = _currentUserService.Principal;
        if (user?.Identity?.Name == null) return "Error: User is not authenticated.";

        var patients = await _patientRepository.FindAsync(p => p.Email == user.Identity.Name);
        var patient = patients.FirstOrDefault();
        if (patient == null) return "Error: Patient record not found for the current user.";

        var appointment = await _appointmentRepository.GetByIdAsync(appointmentId);
        if (appointment == null) return $"Error: Appointment with ID {appointmentId} not found.";
        if (appointment.PatientId != patient.Id) return "Error: You are not authorized to cancel this appointment.";

        // Code-enforced confirmation: never cancel until the caller passes confirmed=true.
        if (!confirmed)
            return $"CONFIRMATION REQUIRED: You are about to cancel appointment {appointmentId} scheduled for "
                 + $"{_clock.Format(appointment.StartTime)}. Show these details to the user and ask them to confirm. "
                 + $"Only if they agree, call cancel_my_appointment again with appointmentId={appointmentId} and confirmed=true.";

        appointment.Cancel();
        await _appointmentRepository.UpdateAsync(appointment);
        _appointmentEventService.NotifyAppointmentsChanged();
        return $"Successfully canceled appointment {appointmentId} ({_clock.Format(appointment.StartTime)}).";
    }
}
