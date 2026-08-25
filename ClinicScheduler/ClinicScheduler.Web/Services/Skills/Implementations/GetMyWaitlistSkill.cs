using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>Read-only: lists the current patient's active waitlist entries.</summary>
public sealed class GetMyWaitlistSkill : ISkill
{
    private readonly ICurrentUserService _currentUserService;
    private readonly ClinicDbContext _dbContext;

    public GetMyWaitlistSkill(ICurrentUserService currentUserService, ClinicDbContext dbContext)
    {
        _currentUserService = currentUserService;
        _dbContext = dbContext;
    }

    public string Name => "get_my_waitlist";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "get_my_waitlist",
            ["description"] = "Lists the currently logged in patient's active waitlist entries.",
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

        var patient = await _dbContext.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Email == user.Identity.Name);
        if (patient == null) return "Error: Patient record not found for the current user.";

        var entries = await _dbContext.WaitlistEntries
            .AsNoTracking()
            .Include(w => w.Therapist)
            .Where(w => w.PatientId == patient.Id && w.Status == WaitlistStatus.Active)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync();

        if (entries.Count == 0) return "You have no active waitlist entries.";

        var sb = new System.Text.StringBuilder("Active waitlist entries:\n");
        foreach (var w in entries)
        {
            var therapistPref = w.Therapist != null ? $"{w.Therapist.FirstName} {w.Therapist.LastName}" : "any therapist";
            var timePref = (w.PreferredTimeFrom, w.PreferredTimeTo) switch
            {
                ({ } f, { } t) => $", {f:h:mm tt}–{t:h:mm tt}",
                ({ } f, null) => $", from {f:h:mm tt}",
                (null, { } t) => $", before {t:h:mm tt}",
                _ => ""
            };
            sb.AppendLine($"- ID: {w.Id}, {w.EarliestDate:MMM d, yyyy} – {w.LatestDate:MMM d, yyyy}{timePref}, with {therapistPref}");
        }
        return sb.ToString();
    }
}
