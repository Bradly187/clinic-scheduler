namespace ClinicScheduler.Core.Interfaces;

/// <summary>
/// Records audit events that the automatic change-tracking pipeline cannot see,
/// primarily read access to protected health information (HIPAA access logging).
/// </summary>
public interface IAuditLogger
{
    /// <summary>Records that the current user accessed (read) the given entity.</summary>
    Task LogAccessAsync(string entityName, string entityId, string? detail = null, CancellationToken ct = default);
}
