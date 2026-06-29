using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Staff/Admin: creates a treatment plan (2/3/4 per week, 20/30/50 sessions) for a patient.</summary>
public sealed class CreateTreatmentPlanSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;

    public CreateTreatmentPlanSkill(ICurrentUserService currentUserService, ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    public string Name => "create_treatment_plan";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "create_treatment_plan",
            ["description"] = "Creates a treatment plan for a patient. Staff/Admin only. Frequency must be 2, 3, or 4 sessions per week; total sessions must be 20, 30, or 50.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["patientName"] = new JsonObject { ["type"] = "string", ["description"] = "Full or partial name of the patient." },
                    ["therapistName"] = new JsonObject { ["type"] = "string", ["description"] = "Full or partial name of the assigned therapist." },
                    ["frequencyPerWeek"] = new JsonObject { ["type"] = "integer", ["description"] = "Sessions per week: 2, 3, or 4." },
                    ["totalDays"] = new JsonObject { ["type"] = "integer", ["description"] = "Total sessions in the plan: 20, 30, or 50." },
                    ["startDate"] = new JsonObject { ["type"] = "string", ["description"] = "Plan start date in YYYY-MM-DD format." },
                    ["therapyTypeName"] = new JsonObject { ["type"] = "string", ["description"] = "Optional therapy type to include in the plan." }
                },
                ["required"] = new JsonArray { "patientName", "therapistName", "frequencyPerWeek", "totalDays", "startDate" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var patientName = arguments?["patientName"]?.GetValue<string>();
        var therapistName = arguments?["therapistName"]?.GetValue<string>();
        var frequencyPerWeek = SkillArgs.ParseOptionalInt(arguments?["frequencyPerWeek"]) ?? 0;
        var totalDays = SkillArgs.ParseOptionalInt(arguments?["totalDays"]) ?? 0;
        var startDate = arguments?["startDate"]?.GetValue<string>();
        var therapyTypeName = arguments?["therapyTypeName"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (!user.IsStaffOrAbove()) return "Error: Unauthorized. Only Staff or Admins can create treatment plans.";

        if (string.IsNullOrWhiteSpace(patientName)) return "Error: patientName is required.";
        if (string.IsNullOrWhiteSpace(therapistName)) return "Error: therapistName is required.";
        if (frequencyPerWeek is not (2 or 3 or 4)) return "Error: frequencyPerWeek must be 2, 3, or 4.";
        if (totalDays is not (20 or 30 or 50)) return "Error: totalDays must be 20, 30, or 50.";
        if (string.IsNullOrWhiteSpace(startDate)) return "Error: startDate is required (YYYY-MM-DD).";
        if (!DateOnly.TryParse(startDate, out var parsedStart))
            return $"Error: Could not parse startDate '{startDate}'. Use YYYY-MM-DD format.";

        var patients = await _dbContext.Patients
            .Where(p => (p.FirstName + " " + p.LastName).Contains(patientName)
                     || p.FirstName.Contains(patientName) || p.LastName.Contains(patientName)).ToListAsync();
        if (patients.Count == 0) return $"Error: No patient found matching '{patientName}'.";
        if (patients.Count > 1)
            return $"Multiple patients match '{patientName}': {string.Join(", ", patients.Select(p => $"{p.FirstName} {p.LastName} (ID:{p.Id})"))}. Please be more specific.";
        var patient = patients[0];

        var therapists = await _dbContext.Therapists
            .Where(t => (t.FirstName + " " + t.LastName).Contains(therapistName)
                     || t.FirstName.Contains(therapistName) || t.LastName.Contains(therapistName)).ToListAsync();
        if (therapists.Count == 0) return $"Error: No therapist found matching '{therapistName}'.";
        if (therapists.Count > 1)
            return $"Multiple therapists match '{therapistName}': {string.Join(", ", therapists.Select(t => $"{t.FirstName} {t.LastName}"))}. Please be more specific.";
        var therapist = therapists[0];

        try
        {
            var plan = new TreatmentPlan(patient, therapist, frequencyPerWeek, totalDays, parsedStart);

            if (!string.IsNullOrWhiteSpace(therapyTypeName))
            {
                var therapyType = await _dbContext.TherapyTypes.FirstOrDefaultAsync(tt => tt.Name.Contains(therapyTypeName));
                if (therapyType == null) return $"Error: No therapy type found matching '{therapyTypeName}'.";
                plan.AddTherapy(therapyType);
            }

            _dbContext.TreatmentPlans.Add(plan);
            await _dbContext.SaveChangesAsync();

            return $"Created treatment plan {plan.Id} for {patient.FirstName} {patient.LastName}: "
                 + $"{plan.FrequencyPerWeek}x/week for {plan.TotalDays} sessions, {plan.StartDate:MMM d, yyyy} – {plan.EndDate:MMM d, yyyy}, "
                 + $"therapist {therapist.FirstName} {therapist.LastName}. "
                 + $"Use generate_plan_appointments to book the session series.";
        }
        catch (ArgumentException ex)
        {
            return $"Could not create treatment plan: {ex.Message}";
        }
    }
}
