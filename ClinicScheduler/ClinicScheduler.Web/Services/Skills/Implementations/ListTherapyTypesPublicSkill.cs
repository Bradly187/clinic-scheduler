using System.Text;
using System.Text.Json.Nodes;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Web.Services.Skills.Implementations;

/// <summary>
/// Public-facing skill: lists therapy types offered by the clinic.
/// </summary>
public sealed class ListTherapyTypesPublicSkill : ISkill
{
    private readonly IRepository<TherapyType> _therapyTypeRepo;

    public ListTherapyTypesPublicSkill(IRepository<TherapyType> therapyTypeRepo)
    {
        _therapyTypeRepo = therapyTypeRepo;
    }

    public string Name => "list_therapy_types_public";

    public JsonObject GetSchema() => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = "list_therapy_types_public",
            ["description"] = "Lists the types of therapy sessions offered by the clinic.",
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
        var types = await _therapyTypeRepo.GetAllAsync();

        if (types.Count == 0)
            return "No therapy types are currently configured.";

        var sb = new StringBuilder("We offer the following therapy types:\n");
        foreach (var t in types)
        {
            sb.Append($"- {t.Name}");
            if (!string.IsNullOrWhiteSpace(t.Description))
                sb.Append($": {t.Description}");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
