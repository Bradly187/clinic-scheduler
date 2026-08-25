using System.Text.Json.Nodes;

namespace ClinicScheduler.Web.Services.Skills;

public interface ISkillExecutor
{
    JsonObject GetToolSchema(string skillName);
    Task<string> ExecuteAsync(string skillName, JsonObject? arguments);
}

/// <summary>
/// Thin dispatcher over the registered <see cref="ISkill"/> set. It owns no clinic logic — each
/// skill carries its own schema and execution. Skills self-register in DI, so adding a tool no
/// longer means editing a central switch here.
/// </summary>
public class SkillExecutor : ISkillExecutor
{
    private readonly IReadOnlyDictionary<string, ISkill> _skills;

    public SkillExecutor(IEnumerable<ISkill> skills)
        => _skills = skills.ToDictionary(s => s.Name);

    /// <summary>Returns the named skill's tool schema, or throws if no skill is registered under that name.</summary>
    public JsonObject GetToolSchema(string skillName)
        => _skills.TryGetValue(skillName, out var skill)
            ? skill.GetSchema()
            : throw new ArgumentException($"Unknown skill: {skillName}");

    /// <summary>Runs the named skill, mirroring the original error-wrapping contract.</summary>
    public async Task<string> ExecuteAsync(string skillName, JsonObject? arguments)
    {
        if (!_skills.TryGetValue(skillName, out var skill))
            return $"Error: Unknown skill {skillName}.";

        try
        {
            return await skill.ExecuteAsync(arguments);
        }
        catch (Exception ex)
        {
            return $"Error executing skill {skillName}: {ex.Message}";
        }
    }
}
