using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Staff/Admin: lists a named patient's upcoming scheduled appointments.</summary>
public sealed class GetAppointmentsSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Patient> _patientRepository;
    private readonly ClinicDbContext _dbContext;
    private readonly IClinicTimeFormatter _clock;

    public GetAppointmentsSkill(
        ICurrentUserService currentUserService,
        IRepository<Patient> patientRepository,
        ClinicDbContext dbContext,
        IClinicTimeFormatter clock)
    {
        _currentUserService = currentUserService;
        _patientRepository = patientRepository;
        _dbContext = dbContext;
        _clock = clock;
    }

    public string Name => "get_appointments";

    public JsonObject GetSchema() => new()
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
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var patientName = arguments?["patientName"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (!user.IsStaffOrAbove()) return "Error: Unauthorized. Only Staff or Admins can retrieve patient appointments.";
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
            sb.AppendLine($"- ID: {apt.Id}, {_clock.Format(apt.StartTime)} (ends {apt.EndTime:h:mm tt}), Therapist: {therapistName}, Room: {roomName}");
        }
        return sb.ToString();
    }
}
