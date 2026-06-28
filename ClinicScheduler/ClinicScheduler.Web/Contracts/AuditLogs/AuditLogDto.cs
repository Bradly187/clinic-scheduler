using ClinicScheduler.Core.Entities;

namespace ClinicScheduler.Web.Contracts.AuditLogs;

public sealed class AuditLogDto
{
    public int Id { get; init; }
    public string EntityName { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string? ChangeSummary { get; init; }
    public DateTime Timestamp { get; init; }
    public string? UserId { get; init; }

    public static AuditLogDto From(AuditLog log) => new()
    {
        Id = log.Id,
        EntityName = log.EntityName,
        EntityId = log.EntityId,
        Action = log.Action.ToString(),
        ChangeSummary = log.ChangeSummary,
        Timestamp = log.Timestamp,
        UserId = log.UserId,
    };
}
