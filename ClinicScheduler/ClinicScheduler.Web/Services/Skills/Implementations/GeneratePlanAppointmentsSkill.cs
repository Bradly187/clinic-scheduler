using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Staff/Admin: books the recurring appointment series for a treatment plan in one shot.</summary>
public sealed class GeneratePlanAppointmentsSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Room> _roomRepository;
    private readonly TreatmentPlanScheduleService _treatmentPlanScheduleService;
    private readonly IAppointmentEventService _appointmentEventService;

    public GeneratePlanAppointmentsSkill(
        ICurrentUserService currentUserService,
        IRepository<Room> roomRepository,
        TreatmentPlanScheduleService treatmentPlanScheduleService,
        IAppointmentEventService appointmentEventService)
    {
        _currentUserService = currentUserService;
        _roomRepository = roomRepository;
        _treatmentPlanScheduleService = treatmentPlanScheduleService;
        _appointmentEventService = appointmentEventService;
    }

    public string Name => "generate_plan_appointments";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "generate_plan_appointments",
            ["description"] = "Books the recurring appointment series for a treatment plan (composite: schedules many sessions at once). Staff/Admin only. Reports how many sessions were booked vs. left unbooked.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["treatmentPlanId"] = new JsonObject { ["type"] = "integer", ["description"] = "The ID of the treatment plan to generate appointments for." },
                    ["preferredTime"] = new JsonObject { ["type"] = "string", ["description"] = "Optional preferred start time in HH:MM (24-hour). Defaults to 09:00." }
                },
                ["required"] = new JsonArray { "treatmentPlanId" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var treatmentPlanId = SkillArgs.ParseOptionalInt(arguments?["treatmentPlanId"]) ?? 0;
        var preferredTimeStr = arguments?["preferredTime"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (!user.IsStaffOrAbove()) return "Error: Unauthorized. Only Staff or Admins can generate plan appointments.";

        var preferredTime = new TimeOnly(9, 0);
        if (!string.IsNullOrWhiteSpace(preferredTimeStr))
        {
            if (!TimeOnly.TryParse(preferredTimeStr, out var t))
                return $"Error: Could not parse preferredTime '{preferredTimeStr}'. Use HH:MM (24-hour) format.";
            preferredTime = t;
        }

        var rooms = await _roomRepository.GetAllAsync();
        var room = rooms.FirstOrDefault();
        if (room == null) return "Error: No rooms are available in the system.";

        try
        {
            var result = await _treatmentPlanScheduleService.GenerateAppointmentsAsync(
                treatmentPlanId, room.Id, preferredTime);

            _appointmentEventService.NotifyAppointmentsChanged();

            var summary = $"Generated appointments for treatment plan {treatmentPlanId}: "
                        + $"booked {result.SessionsBooked} of {result.SessionsRequested} session(s).";
            if (result.SessionsUnbooked > 0)
                summary += $" {result.SessionsUnbooked} could not be placed (clinic full within the search window) — try a different time or book them manually.";
            return summary;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"Could not generate plan appointments: {ex.Message}";
        }
    }
}
