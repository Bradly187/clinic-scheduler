using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Adds a patient to the waitlist for a date window; the system books the first match automatically.</summary>
public sealed class JoinWaitlistSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;

    public JoinWaitlistSkill(ICurrentUserService currentUserService, ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    public string Name => "join_waitlist";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "join_waitlist",
            ["description"] = "Adds the patient to the waitlist for a date window. When a matching slot opens up, the system books it automatically. Patients join for themselves; Staff/Admin can specify a patientName.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["earliestDate"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Earliest date the patient can attend, in YYYY-MM-DD format."
                    },
                    ["latestDate"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Latest date the patient can attend, in YYYY-MM-DD format (window must be within 90 days of the earliest date)."
                    },
                    ["therapistName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional preferred therapist (full or partial name). Omit for any therapist."
                    },
                    ["preferredTimeFrom"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional earliest acceptable start time in HH:MM (24-hour). Omit for any time."
                    },
                    ["preferredTimeTo"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional latest acceptable start time in HH:MM (24-hour). Omit for any time."
                    },
                    ["notes"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Optional notes about the request."
                    },
                    ["patientName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the patient. Staff/Admin only — patients are added under their own account."
                    }
                },
                ["required"] = new JsonArray { "earliestDate", "latestDate" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var earliestDate = arguments?["earliestDate"]?.GetValue<string>();
        var latestDate = arguments?["latestDate"]?.GetValue<string>();
        var therapistName = arguments?["therapistName"]?.GetValue<string>();
        var preferredTimeFrom = arguments?["preferredTimeFrom"]?.GetValue<string>();
        var preferredTimeTo = arguments?["preferredTimeTo"]?.GetValue<string>();
        var notes = arguments?["notes"]?.GetValue<string>();
        var patientName = arguments?["patientName"]?.GetValue<string>();

        var user = _currentUserService.Principal;
        if (user == null) return "Error: User is not authenticated.";

        if (string.IsNullOrWhiteSpace(earliestDate)) return "Error: earliestDate is required (YYYY-MM-DD).";
        if (string.IsNullOrWhiteSpace(latestDate)) return "Error: latestDate is required (YYYY-MM-DD).";
        if (!DateOnly.TryParse(earliestDate, out var earliest))
            return $"Error: Could not parse earliestDate '{earliestDate}'. Use YYYY-MM-DD format.";
        if (!DateOnly.TryParse(latestDate, out var latest))
            return $"Error: Could not parse latestDate '{latestDate}'. Use YYYY-MM-DD format.";

        TimeOnly? timeFrom = null, timeTo = null;
        if (!string.IsNullOrWhiteSpace(preferredTimeFrom))
        {
            if (!TimeOnly.TryParse(preferredTimeFrom, out var f)) return $"Error: Could not parse preferredTimeFrom '{preferredTimeFrom}'. Use HH:MM.";
            timeFrom = f;
        }
        if (!string.IsNullOrWhiteSpace(preferredTimeTo))
        {
            if (!TimeOnly.TryParse(preferredTimeTo, out var t)) return $"Error: Could not parse preferredTimeTo '{preferredTimeTo}'. Use HH:MM.";
            timeTo = t;
        }

        // Resolve patient (tracked, so EF doesn't try to re-insert it as a new patient).
        Patient? patient;
        if (user.IsStaffOrAbove() && !string.IsNullOrWhiteSpace(patientName))
        {
            var matches = await _dbContext.Patients
                .Where(p => p.FirstName.Contains(patientName) || p.LastName.Contains(patientName))
                .ToListAsync();
            if (matches.Count == 0) return $"Error: No patient found matching '{patientName}'.";
            if (matches.Count > 1)
                return $"Multiple patients match '{patientName}': {string.Join(", ", matches.Select(p => $"{p.FirstName} {p.LastName} (ID:{p.Id})"))}. Please be more specific.";
            patient = matches[0];
        }
        else
        {
            if (user.Identity?.Name == null) return "Error: User is not authenticated.";
            patient = await _dbContext.Patients.FirstOrDefaultAsync(p => p.Email == user.Identity.Name);
            if (patient == null) return "Error: Patient record not found for the current user.";
        }

        // Resolve optional preferred therapist (tracked).
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

        try
        {
            var entry = new WaitlistEntry(patient, earliest, latest, therapist, location: null, timeFrom, timeTo, notes);
            _dbContext.WaitlistEntries.Add(entry);
            await _dbContext.SaveChangesAsync();

            var therapistPref = therapist != null ? $"{therapist.FirstName} {therapist.LastName}" : "any therapist";
            var timePref = (timeFrom, timeTo) switch
            {
                ({ } f, { } t) => $" between {f:h:mm tt} and {t:h:mm tt}",
                ({ } f, null) => $" from {f:h:mm tt}",
                (null, { } t) => $" before {t:h:mm tt}",
                _ => ""
            };
            return $"Added to the waitlist (entry ID {entry.Id}) for {patient.FirstName} {patient.LastName}: "
                 + $"{earliest:MMM d, yyyy} – {latest:MMM d, yyyy}{timePref}, with {therapistPref}. "
                 + $"The first matching opening will be booked automatically.";
        }
        catch (ArgumentException ex)
        {
            return $"Could not join the waitlist: {ex.Message}";
        }
    }
}
