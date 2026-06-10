using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Infrastructure.Data;

/// <summary>
/// Writes explicit audit events (e.g. PHI read access) attributed to the current user.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ClinicDbContext _context;
    private readonly ICurrentUserService? _currentUser;

    public AuditLogger(ClinicDbContext context, ICurrentUserService? currentUser = null)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task LogAccessAsync(string entityName, string entityId, string? detail = null, CancellationToken ct = default)
    {
        var userId = _currentUser?.UserId ?? _currentUser?.UserName;
        _context.AuditLogs.Add(new AuditLog(entityName, entityId, AuditAction.Accessed, detail, userId));
        await _context.SaveChangesAsync(ct);
    }
}
