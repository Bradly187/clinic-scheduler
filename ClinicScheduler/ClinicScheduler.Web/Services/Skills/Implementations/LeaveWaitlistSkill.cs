using System.Text.Json.Nodes;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Removes one of the current patient's own waitlist entries by ID.</summary>
public sealed class LeaveWaitlistSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;

    public LeaveWaitlistSkill(ICurrentUserService currentUserService, ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    public string Name => "leave_waitlist";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "leave_waitlist",
            ["description"] = "Removes one of the currently logged in patient's waitlist entries, given its ID.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["waitlistEntryId"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "The ID of the waitlist entry to remove."
                    }
                },
                ["required"] = new JsonArray { "waitlistEntryId" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var waitlistEntryId = SkillArgs.ParseOptionalInt(arguments?["waitlistEntryId"]) ?? 0;

        var user = _currentUserService.Principal;
        if (user?.Identity?.Name == null) return "Error: User is not authenticated.";

        var patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Email == user.Identity.Name);
        if (patient == null) return "Error: Patient record not found for the current user.";

        var entry = await _dbContext.WaitlistEntries.FindAsync(waitlistEntryId);
        if (entry == null) return $"Error: Waitlist entry with ID {waitlistEntryId} not found.";
        if (entry.PatientId != patient.Id) return "Error: You are not authorized to remove this waitlist entry.";

        try
        {
            entry.Cancel();
            await _dbContext.SaveChangesAsync();
            return $"Removed waitlist entry {waitlistEntryId}.";
        }
        catch (InvalidOperationException ex)
        {
            return $"Could not remove waitlist entry: {ex.Message}";
        }
    }
}
