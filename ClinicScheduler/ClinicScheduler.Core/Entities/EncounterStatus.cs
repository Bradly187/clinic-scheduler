namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Lifecycle of a patient <see cref="Encounter"/>. Maps onto the FHIR R4 Encounter status
/// value set (planned / in-progress / finished / cancelled).
/// </summary>
public enum EncounterStatus
{
    /// <summary>Scheduled/registered but not yet started.</summary>
    Planned,

    /// <summary>The patient has arrived and the encounter is underway.</summary>
    InProgress,

    /// <summary>The encounter has completed.</summary>
    Finished,

    /// <summary>The encounter was cancelled before completion.</summary>
    Cancelled
}
