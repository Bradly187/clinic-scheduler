namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// Definition of a specialist sub-agent: a focused role with its own system prompt and a
/// subset of the clinic skills it is allowed to use.
/// </summary>
/// <param name="Name">Lower-snake-case id; the coordinator routes to it via <c>route_to_{Name}</c>.</param>
/// <param name="Description">What this specialist handles — shown to the coordinator for routing.</param>
/// <param name="SystemPrompt">Role instructions for the specialist's own tool loop.</param>
/// <param name="SkillNames">Skills (tools) this specialist may call. Empty = advisory only.</param>
public sealed record SpecialistAgent(string Name, string Description, string SystemPrompt, string[] SkillNames);
