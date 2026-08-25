using System.Text;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Public-facing skill: lists therapists and their specialties.
/// Does not expose emails, IDs, or internal data.
/// </summary>
public sealed class ListTherapistsPublicSkill : ISkill
{
    private readonly IRepository<Therapist> _therapistRepo;

    public ListTherapistsPublicSkill(IRepository<Therapist> therapistRepo)
    {
        _therapistRepo = therapistRepo;
    }

    public string Name => "list_therapists_public";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "list_therapists_public",
            ["description"] = "Lists available therapists and their specialties. Use this to help the patient choose who they'd like to see.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["required"] = new JsonArray()
            }
        }
    };

    public async Task<string> ExecuteAsync(JsonObject? arguments)
    {
        var therapists = await _therapistRepo.GetAllAsync();

        if (therapists.Count == 0)
            return "No therapists are currently available.";

        var sb = new StringBuilder("Our therapists:\n");
        foreach (var t in therapists.Where(t => !string.IsNullOrWhiteSpace(t.Specialty)))
        {
            sb.AppendLine($"- {t.FirstName} {t.LastName} — {t.Specialty}");
        }

        return sb.ToString();
    }
}
