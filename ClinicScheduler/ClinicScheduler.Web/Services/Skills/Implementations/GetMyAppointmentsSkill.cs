using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Read-only: lists the current patient's upcoming appointments.</summary>
public sealed class GetMyAppointmentsSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IRepository<Patient> _patientRepository;
    private readonly ClinicDbContext _dbContext;
    private readonly IClinicTimeFormatter _clock;

    public GetMyAppointmentsSkill(
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

    public string Name => "get_my_appointments";

    public JsonObject GetSchema() => new()
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
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
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
            sb.AppendLine($"- ID: {apt.Id}, {_clock.Format(apt.StartTime)} (ends {apt.EndTime:h:mm tt}), Therapist: {therapistName}, Room: {roomName}, Status: {apt.Status}");
        }
        return sb.ToString();
    }
}
