using System.Text.Json.Nodes;

namespace ClinicScheduler.Web.Services.Skills;

/// <summary>
/// A single agent tool ("skill"): it owns both its OpenAI-format function schema and its
/// execution logic. Skills self-register in DI; <see cref="SkillExecutor"/> simply dispatches
/// to the one whose <see cref="Name"/> matches.
///
/// Adding a new tool is now "add an <see cref="ISkill"/> class + register it" — no central
/// switch to edit. Each implementation injects only the dependencies it actually needs.
/// </summary>
public interface ISkill
{
    /// <summary>The tool name the model calls (must match the name inside <see cref="GetSchema"/>).</summary>
    string Name { get; }

    /// <summary>The OpenAI-format function tool schema offered to the model.</summary>
    JsonObject GetSchema();

    /// <summary>Runs the skill against the model-supplied arguments and returns a text result.</summary>
    Task<string> ExecuteAsync(JsonObject? arguments);
}
