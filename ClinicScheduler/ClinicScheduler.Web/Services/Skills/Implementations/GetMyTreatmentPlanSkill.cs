using System.Text.Json.Nodes;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Shows a patient's current treatment plan (patients see their own; Staff/Admin pass a name).</summary>
public sealed class GetMyTreatmentPlanSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;

    public GetMyTreatmentPlanSkill(ICurrentUserService currentUserService, ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    public string Name => "get_my_treatment_plan";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "get_my_treatment_plan",
            ["description"] = "Retrieves the current treatment plan for a patient (frequency, duration, therapist, therapies, status). Patients see their own; Staff/Admin can pass a patientName.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["patientName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the patient. Staff/Admin only — patients see their own plan automatically."
                    }
                }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var patientName = arguments?["patientName"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        int patientId;
        string patientLabel;
        if (user.IsStaffOrAbove() && !string.IsNullOrWhiteSpace(patientName))
        {
            var matches = await _dbContext.Patients
                .Where(p => (p.FirstName + " " + p.LastName).Contains(patientName)
                         || p.FirstName.Contains(patientName) || p.LastName.Contains(patientName))
                .ToListAsync();
            if (matches.Count == 0) return $"Error: No patient found matching '{patientName}'.";
            if (matches.Count > 1)
                return $"Multiple patients match '{patientName}': {string.Join(", ", matches.Select(p => $"{p.FirstName} {p.LastName} (ID:{p.Id})"))}. Please be more specific.";
            patientId = matches[0].Id;
            patientLabel = $"{matches[0].FirstName} {matches[0].LastName}";
        }
        else
        {
            if (user.Identity?.Name == null) return "Error: User is not authenticated.";
            var patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.Email == user.Identity.Name);
            if (patient == null) return "Error: Patient record not found for the current user.";
            patientId = patient.Id;
            patientLabel = $"{patient.FirstName} {patient.LastName}";
        }

        var plan = await _dbContext.TreatmentPlans
            .AsNoTracking()
            .Include(p => p.Therapist)
            .Include(p => p.TreatmentPlanTherapies)
                .ThenInclude(tpt => tpt.TherapyType)
            .Where(p => p.PatientId == patientId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (plan == null) return $"No treatment plan on file for {patientLabel}.";

        var therapist = plan.Therapist != null ? $"{plan.Therapist.FirstName} {plan.Therapist.LastName}" : "Unassigned";
        var therapies = plan.TreatmentPlanTherapies
            .Select(tpt => tpt.TherapyType?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n));
        var therapyList = therapies.Any() ? string.Join(", ", therapies) : "none specified";

        return $"Treatment plan for {patientLabel} (ID {plan.Id}): {plan.FrequencyPerWeek}x/week for {plan.TotalDays} sessions, "
             + $"{plan.StartDate:MMM d, yyyy} – {plan.EndDate:MMM d, yyyy}, therapist {therapist}, "
             + $"status {plan.Status}. Therapies: {therapyList}.";
    }
}
