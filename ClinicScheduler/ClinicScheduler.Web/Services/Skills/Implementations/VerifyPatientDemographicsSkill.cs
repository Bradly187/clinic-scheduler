using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Intake (read-only): returns a patient's current demographics so they can be confirmed before
/// a change. Staff/Admin may look up anyone by name or email; a patient sees only their own record.
/// </summary>
public sealed class VerifyPatientDemographicsSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;

    public VerifyPatientDemographicsSkill(ICurrentUserService currentUserService, ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    public string Name => "verify_patient_demographics";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "verify_patient_demographics",
            ["description"] = "Reads back a patient's current demographics (name, email, phone, date of birth, SMS consent) so they can be confirmed before registering changes. Patients see their own; Staff/Admin can pass a patientName or email.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["patientName"] = new JsonObject { ["type"] = "string", ["description"] = "Full or partial name. Staff/Admin only." },
                    ["email"] = new JsonObject { ["type"] = "string", ["description"] = "Exact email. Staff/Admin only." }
                }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var patientName = arguments?["patientName"]?.GetValue<string>();
        var email = arguments?["email"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        Patient? patient;
        if (user.IsStaffOrAbove())
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Email == email);
                if (patient == null) return $"Error: No patient found with email '{email}'.";
            }
            else if (!string.IsNullOrWhiteSpace(patientName))
            {
                var matches = await _dbContext.Patients.AsNoTracking()
                    .Where(p => (p.FirstName + " " + p.LastName).Contains(patientName)
                             || p.FirstName.Contains(patientName) || p.LastName.Contains(patientName))
                    .ToListAsync();
                if (matches.Count == 0) return $"Error: No patient found matching '{patientName}'.";
                if (matches.Count > 1)
                    return $"Multiple patients match '{patientName}': {string.Join(", ", matches.Select(p => $"{p.FirstName} {p.LastName} (ID:{p.Id})"))}. Please be more specific.";
                patient = matches[0];
            }
            else
            {
                return "Error: Provide a patientName or email to look up.";
            }
        }
        else
        {
            if (user.Identity?.Name == null) return "Error: User is not authenticated.";
            patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Email == user.Identity.Name);
            if (patient == null) return "Error: No patient record is linked to your account.";
        }

        var phone = string.IsNullOrWhiteSpace(patient.Phone) ? "(none)" : patient.Phone;
        var consent = patient.SmsRemindersConsent ? "yes" : "no";
        return $"Patient {patient.FirstName} {patient.LastName} (ID {patient.Id}): "
             + $"email {patient.Email}, phone {phone}, DOB {patient.DateOfBirth:MMM d, yyyy}, SMS reminders consent: {consent}.";
    }
}
