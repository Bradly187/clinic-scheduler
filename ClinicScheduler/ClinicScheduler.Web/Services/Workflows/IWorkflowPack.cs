namespace ClinicScheduler.Web.Services.Workflows;

/// <summary>
/// A self-contained healthcare workflow contributed to the agent. A pack bundles one or more
/// <see cref="SpecialistAgent"/>s (and, in later sprints, their skills and domain). The
/// orchestrator's coordinator routes across the specialists of <b>all</b> registered packs, so
/// adding a workflow is "register a pack" — no edit to the orchestrator.
/// </summary>
public interface IWorkflowPack
{
    /// <summary>Stable lower-snake-case id for the pack (diagnostics / grouping).</summary>
    string Name { get; }

    /// <summary>The specialist sub-agents this pack contributes to the coordinator's roster.</summary>
    IEnumerable<SpecialistAgent> GetSpecialists();
}
