using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Intake: registers a new patient or updates an existing one's demographics + SMS consent.
/// Staff/Admin may register/update anyone; a Patient may only update their own record (and cannot
/// create a new one).
/// </summary>
public sealed class RegisterPatientSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;

    public RegisterPatientSkill(ICurrentUserService currentUserService, ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    public string Name => "register_patient";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "register_patient",
            ["description"] = "Registers a new patient or updates an existing patient's demographics. Staff/Admin can register or update any patient; a patient can update their own record but cannot create a new one.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["firstName"] = new JsonObject { ["type"] = "string", ["description"] = "Patient's first name." },
                    ["lastName"] = new JsonObject { ["type"] = "string", ["description"] = "Patient's last name." },
                    ["email"] = new JsonObject { ["type"] = "string", ["description"] = "Patient's email (unique identifier). Staff/Admin only — a patient's own record is matched to their login." },
                    ["dateOfBirth"] = new JsonObject { ["type"] = "string", ["description"] = "Date of birth in YYYY-MM-DD format." },
                    ["phone"] = new JsonObject { ["type"] = "string", ["description"] = "Optional phone number." },
                    ["smsConsent"] = new JsonObject { ["type"] = "boolean", ["description"] = "Optional: whether the patient consents to SMS reminders." }
                },
                ["required"] = new JsonArray { "firstName", "lastName", "dateOfBirth" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var firstName = arguments?["firstName"]?.GetValue<string>();
        var lastName = arguments?["lastName"]?.GetValue<string>();
        var email = arguments?["email"]?.GetValue<string>();
        var dateOfBirth = arguments?["dateOfBirth"]?.GetValue<string>();
        var phone = arguments?["phone"]?.GetValue<string>();
        bool? smsConsent = arguments?["smsConsent"] is { } sc ? SkillArgs.ParseBool(sc) : null;

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (string.IsNullOrWhiteSpace(firstName)) return "Error: firstName is required.";
        if (string.IsNullOrWhiteSpace(lastName)) return "Error: lastName is required.";
        if (string.IsNullOrWhiteSpace(dateOfBirth)) return "Error: dateOfBirth is required (YYYY-MM-DD).";
        if (!DateOnly.TryParse(dateOfBirth, out var dob))
            return $"Error: Could not parse dateOfBirth '{dateOfBirth}'. Use YYYY-MM-DD format.";

        var isStaff = user.IsStaffOrAbove();

        // Patient role: only ever updates their own record, matched by their login email.
        if (!isStaff)
        {
            if (user.Identity?.Name == null) return "Error: User is not authenticated.";
            var own = await _dbContext.Patients.FirstOrDefaultAsync(p => p.Email == user.Identity.Name);
            if (own == null) return "Error: No patient record is linked to your account. Please ask staff to register you.";

            own.UpdateDetails(firstName, lastName, dob);
            own.UpdateContactInfo(string.IsNullOrWhiteSpace(email) ? own.Email : email, phone);
            if (smsConsent.HasValue) own.SetSmsConsent(smsConsent.Value);
            await _dbContext.SaveChangesAsync();
            return $"Updated your patient record (ID {own.Id}): {own.FirstName} {own.LastName}, DOB {own.DateOfBirth:MMM d, yyyy}.";
        }

        // Staff/Admin: update by email if the patient exists, otherwise create.
        if (string.IsNullOrWhiteSpace(email)) return "Error: email is required to register or update a patient.";

        var existing = await _dbContext.Patients.FirstOrDefaultAsync(p => p.Email == email);
        if (existing != null)
        {
            existing.UpdateDetails(firstName, lastName, dob);
            existing.UpdateContactInfo(email, phone);
            if (smsConsent.HasValue) existing.SetSmsConsent(smsConsent.Value);
            await _dbContext.SaveChangesAsync();
            return $"Updated patient {existing.FirstName} {existing.LastName} (ID {existing.Id}).";
        }

        var patient = new Patient(firstName, lastName, email, dob, phone);
        if (smsConsent.HasValue) patient.SetSmsConsent(smsConsent.Value);
        _dbContext.Patients.Add(patient);
        await _dbContext.SaveChangesAsync();
        return $"Registered new patient {patient.FirstName} {patient.LastName} (ID {patient.Id}), DOB {patient.DateOfBirth:MMM d, yyyy}.";
    }
}
