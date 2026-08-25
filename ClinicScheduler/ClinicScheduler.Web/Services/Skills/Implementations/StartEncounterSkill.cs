using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Intake (Staff/Admin): opens a new patient encounter (visit) — the front of the patient journey.
/// Resolves the patient by name or email, optionally links a therapist and location, and records
/// the chief-complaint reason. Best-effort syncs the encounter to the EHR as a FHIR Encounter.
/// </summary>
public sealed class StartEncounterSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;
    private readonly IFhirSyncService _fhirSyncService;

    public StartEncounterSkill(
        ICurrentUserService currentUserService,
        ClinicDbContext dbContext,
        IFhirSyncService fhirSyncService)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
        _fhirSyncService = fhirSyncService;
    }

    public string Name => "start_encounter";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "start_encounter",
            ["description"] = "Opens a new intake encounter (visit) for a patient. Staff/Admin only. Resolves the patient by name or email; optionally links a therapist and location and records a reason for the visit.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["patientName"] = new JsonObject { ["type"] = "string", ["description"] = "Full or partial patient name (provide this or email)." },
                    ["email"] = new JsonObject { ["type"] = "string", ["description"] = "Exact patient email (provide this or patientName)." },
                    ["reason"] = new JsonObject { ["type"] = "string", ["description"] = "Optional reason for the visit / chief complaint." },
                    ["therapistName"] = new JsonObject { ["type"] = "string", ["description"] = "Optional therapist to attach to the encounter." },
                    ["locationName"] = new JsonObject { ["type"] = "string", ["description"] = "Optional location of the visit." }
                }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var patientName = arguments?["patientName"]?.GetValue<string>();
        var email = arguments?["email"]?.GetValue<string>();
        var reason = arguments?["reason"]?.GetValue<string>();
        var therapistName = arguments?["therapistName"]?.GetValue<string>();
        var locationName = arguments?["locationName"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";
        if (!user.IsStaffOrAbove()) return "Error: Unauthorized. Only Staff or Admins can open an encounter.";

        // Resolve the patient (tracked, so the new encounter's FK is wired correctly).
        Patient? patient;
        if (!string.IsNullOrWhiteSpace(email))
        {
            patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.Email == email);
            if (patient == null) return $"Error: No patient found with email '{email}'.";
        }
        else if (!string.IsNullOrWhiteSpace(patientName))
        {
            var matches = await _dbContext.Patients
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
            return "Error: Provide a patientName or email to open an encounter for.";
        }

        // Optional therapist.
        Therapist? therapist = null;
        if (!string.IsNullOrWhiteSpace(therapistName))
        {
            var therapists = await _dbContext.Therapists
                .Where(t => t.FirstName.Contains(therapistName) || t.LastName.Contains(therapistName))
                .ToListAsync();
            if (therapists.Count == 0) return $"Error: No therapist found matching '{therapistName}'.";
            if (therapists.Count > 1)
                return $"Multiple therapists match '{therapistName}': {string.Join(", ", therapists.Select(t => $"{t.FirstName} {t.LastName}"))}. Please be more specific.";
            therapist = therapists[0];
        }

        // Optional location.
        Location? location = null;
        if (!string.IsNullOrWhiteSpace(locationName))
        {
            var locations = await _dbContext.Locations.Where(l => l.Name.Contains(locationName)).ToListAsync();
            if (locations.Count == 0) return $"Error: No location found matching '{locationName}'.";
            if (locations.Count > 1)
                return $"Multiple locations match '{locationName}': {string.Join(", ", locations.Select(l => l.Name))}. Please be more specific.";
            location = locations[0];
        }

        var encounter = new Encounter(patient, DateTime.UtcNow, reason, therapist, location);
        _dbContext.Encounters.Add(encounter);
        await _dbContext.SaveChangesAsync();

        // Best-effort EHR sync (never blocks the intake operation).
        _ = _fhirSyncService.SyncEncounterAsync(encounter);

        var withTherapist = therapist != null ? $", therapist {therapist.FirstName} {therapist.LastName}" : "";
        var atLocation = location != null ? $", at {location.Name}" : "";
        var forReason = string.IsNullOrWhiteSpace(reason) ? "" : $" — reason: {reason}";
        return $"Opened intake encounter {encounter.Id} for {patient.FirstName} {patient.LastName} (status {encounter.Status}){withTherapist}{atLocation}{forReason}.";
    }
}
