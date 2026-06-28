using ClinicScheduler.Core.Entities;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Contracts.AuditLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

/// <summary>Read-only access to the HIPAA-style audit log. Scoped to Admin and Auditor roles.</summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = RoleNames.AdminOrAuditor)]
public class AuditLogsController : ControllerBase
{
    private readonly ClinicDbContext _db;

    public AuditLogsController(ClinicDbContext db) => _db = db;

    /// <summary>
    /// Returns audit log entries, newest first.
    /// Supports optional filtering by entity name, action, date range, and user ID.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditLogDto>>> GetAll(
        CancellationToken ct,
        [FromQuery] string? entityName = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? userId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(l => l.EntityName == entityName);

        if (!string.IsNullOrWhiteSpace(action) && Enum.TryParse<AuditAction>(action, ignoreCase: true, out var parsedAction))
            query = query.Where(l => l.Action == parsedAction);

        if (from.HasValue)
            query = query.Where(l => l.Timestamp >= Paging.AsUtc(from.Value));

        if (to.HasValue)
            query = query.Where(l => l.Timestamp <= Paging.AsUtc(to.Value));

        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(l => l.UserId == userId);

        query = query.OrderByDescending(l => l.Timestamp);

        var total = await query.CountAsync(ct);
        Response.Headers[Paging.TotalCountHeader] = total.ToString();

        var paging = Paging.Normalize(page, pageSize);
        if (paging.HasValue)
            query = query.Skip(paging.Value.Skip).Take(paging.Value.Take);

        var rows = await query.ToListAsync(ct);
        return Ok(rows.Select(AuditLogDto.From).ToList());
    }

    /// <summary>Returns a single audit log entry by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AuditLogDto>> GetById(int id, CancellationToken ct)
    {
        var log = await _db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct);
        return log is null ? NotFound() : Ok(AuditLogDto.From(log));
    }
}
