using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Contracts.Waitlist;
using ClinicScheduler.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

/// <summary>Manages the appointment waitlist.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.StaffOrAbove)]
public class WaitlistController : ControllerBase
{
    private readonly ClinicDbContext _dbContext;
    private readonly WaitlistService _waitlistService;
    private readonly WaitlistFulfillmentNotifier _notifier;

    /// <summary>Initializes a new instance of <see cref="WaitlistController"/>.</summary>
    public WaitlistController(
        ClinicDbContext dbContext,
        WaitlistService waitlistService,
        WaitlistFulfillmentNotifier notifier)
    {
        _dbContext = dbContext;
        _waitlistService = waitlistService;
        _notifier = notifier;
    }

    /// <summary>
    /// Returns waitlist entries, optionally filtered by <paramref name="status"/>
    /// and paged via <paramref name="page"/>/<paramref name="pageSize"/>.
    /// Active entries are ordered oldest first (FIFO priority).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WaitlistEntryDto>>> GetAll(
        CancellationToken ct,
        [FromQuery] WaitlistStatus? status = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        IQueryable<WaitlistEntry> query = _dbContext.WaitlistEntries
            .AsNoTracking()
            .Include(w => w.Patient)
            .Include(w => w.Therapist)
            .Include(w => w.Location);

        if (status is not null)
            query = query.Where(w => w.Status == status);

        query = query.OrderBy(w => w.Status).ThenBy(w => w.CreatedAt);

        if (Paging.Normalize(page, pageSize) is { } paging)
        {
            Response.Headers[Paging.TotalCountHeader] = (await query.CountAsync(ct)).ToString();
            query = query.Skip(paging.Skip).Take(paging.Take);
        }

        var entries = await query.ToListAsync(ct);
        return Ok(entries.Select(static w => MapToDto(w)).ToList());
    }

    /// <summary>Returns a single waitlist entry by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<WaitlistEntryDto>> GetById(int id, CancellationToken ct)
    {
        var entry = await _dbContext.WaitlistEntries
            .AsNoTracking()
            .Include(w => w.Patient)
            .Include(w => w.Therapist)
            .Include(w => w.Location)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

        return entry is null ? NotFound() : Ok(MapToDto(entry));
    }

    /// <summary>Adds a patient to the waitlist.</summary>
    [HttpPost]
    public async Task<ActionResult<WaitlistEntryDto>> Create(CreateWaitlistEntryRequest request, CancellationToken ct)
    {
        var patient = await _dbContext.Patients.FindAsync([request.PatientId], ct);
        if (patient is null) return BadRequest("Patient not found.");

        Therapist? therapist = null;
        if (request.TherapistId is { } therapistId)
        {
            therapist = await _dbContext.Therapists.FindAsync([therapistId], ct);
            if (therapist is null) return BadRequest("Therapist not found.");
        }

        Location? location = null;
        if (request.LocationId is { } locationId)
        {
            location = await _dbContext.Locations.FindAsync([locationId], ct);
            if (location is null) return BadRequest("Location not found.");
        }

        WaitlistEntry entry;
        try
        {
            entry = new WaitlistEntry(
                patient,
                request.EarliestDate,
                request.LatestDate,
                therapist,
                location,
                request.PreferredTimeFrom,
                request.PreferredTimeTo,
                request.Notes);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        _dbContext.WaitlistEntries.Add(entry);
        await _dbContext.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { id = entry.Id }, MapToDto(entry));
    }

    /// <summary>Cancels an active waitlist entry.</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        var entry = await _dbContext.WaitlistEntries.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (entry is null) return NotFound();

        try
        {
            entry.Cancel();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }

        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Runs a waitlist sweep immediately: expires stale entries and books open
    /// slots for matching ones. Returns the number of appointments booked.
    /// </summary>
    [HttpPost("process")]
    public async Task<ActionResult<object>> Process(CancellationToken ct)
    {
        var fulfillments = await _waitlistService.ProcessWaitlistAsync(ct);
        await _notifier.NotifyAsync(fulfillments, ct);
        return Ok(new { Booked = fulfillments.Count });
    }

    private static WaitlistEntryDto MapToDto(WaitlistEntry entry) => new()
    {
        Id = entry.Id,
        PatientId = entry.PatientId,
        PatientName = entry.Patient.FullName,
        TherapistId = entry.TherapistId,
        TherapistName = entry.Therapist?.FullName,
        LocationId = entry.LocationId,
        LocationName = entry.Location?.Name,
        EarliestDate = entry.EarliestDate,
        LatestDate = entry.LatestDate,
        PreferredTimeFrom = entry.PreferredTimeFrom,
        PreferredTimeTo = entry.PreferredTimeTo,
        Status = entry.Status,
        Notes = entry.Notes,
        FulfilledAppointmentId = entry.FulfilledAppointmentId,
        CreatedAt = entry.CreatedAt,
        UpdatedAt = entry.UpdatedAt
    };
}
