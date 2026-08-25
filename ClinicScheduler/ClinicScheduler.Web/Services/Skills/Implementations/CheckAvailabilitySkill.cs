using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Shared.Services;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Public-facing skill: checks available appointment slots for a therapist on a date range.
/// Does not expose internal IDs — returns human-readable slot descriptions.
/// </summary>
public sealed class CheckAvailabilitySkill : ISkill
{
    private readonly IRepository<Therapist> _therapistRepo;
    private readonly IRepository<Room> _roomRepo;
    private readonly AppointmentSchedulingService _schedulingService;
    private readonly IClinicTimeFormatter _clock;

    public CheckAvailabilitySkill(
        IRepository<Therapist> therapistRepo,
        IRepository<Room> roomRepo,
        AppointmentSchedulingService schedulingService,
        IClinicTimeFormatter clock)
    {
        _therapistRepo = therapistRepo;
        _roomRepo = roomRepo;
        _schedulingService = schedulingService;
        _clock = clock;
    }

    public string Name => "check_availability";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "check_availability",
            ["description"] = "Checks available appointment slots for a therapist on a specific date. Returns a list of open time slots.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["therapistName"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Full or partial name of the therapist, or 'any' for any available therapist."
                    },
                    ["date"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Date to check availability for in YYYY-MM-DD format."
                    }
                },
                ["required"] = new JsonArray { "date" }
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var therapistName = arguments?["therapistName"]?.GetValue<string>();
        var dateStr = arguments?["date"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(dateStr))
            return "Error: date is required (YYYY-MM-DD).";

        if (!DateOnly.TryParse(dateStr, out var parsedDate))
            return $"Error: Could not parse date '{dateStr}'. Use YYYY-MM-DD format.";

        if (parsedDate < DateOnly.FromDateTime(DateTime.UtcNow))
            return "Error: Cannot check availability for past dates.";

        // Find therapists matching the name (or all if "any")
        IReadOnlyList<Therapist> therapists;
        if (string.IsNullOrWhiteSpace(therapistName) || therapistName.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            therapists = await _therapistRepo.GetAllAsync();
        }
        else
        {
            therapists = await _therapistRepo.FindAsync(
                t => t.FirstName.Contains(therapistName) || t.LastName.Contains(therapistName));
        }

        if (therapists.Count == 0)
            return $"No therapists found matching '{therapistName}'.";

        // Get a room to check slot availability
        var rooms = await _roomRepo.GetAllAsync();
        if (rooms.Count == 0)
            return "No rooms are configured in the system.";

        var room = rooms.First();
        var dateTime = parsedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var results = new System.Text.StringBuilder();
        results.AppendLine($"Available slots for {parsedDate:ddd, MMM d, yyyy}:");

        foreach (var therapist in therapists.Take(5)) // Cap at 5 therapists to avoid huge output
        {
            var slots = await _schedulingService.GetDailySlotStartsAsync(room.Id, therapist.Id, dateTime);
            var slotList = slots.ToList();

            if (slotList.Count > 0)
            {
                results.AppendLine($"\n{therapist.FirstName} {therapist.LastName}{(string.IsNullOrWhiteSpace(therapist.Specialty) ? "" : $" ({therapist.Specialty})")}:");
                foreach (var slot in slotList.Take(10)) // Cap at 10 slots per therapist
                {
                    results.AppendLine($"  • {_clock.Format(slot)}");
                }
                if (slotList.Count > 10)
                    results.AppendLine($"  ... and {slotList.Count - 10} more slots");
            }
        }

        var output = results.ToString();
        return output.Contains("•") ? output : $"No available slots found for {parsedDate:ddd, MMM d, yyyy}. Try a different date.";
    }
}
