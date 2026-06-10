using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Web.Contracts.Locations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicScheduler.Web.Api;

/// <summary>Manages clinic location resources, including scheduling configuration.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LocationsController : ControllerBase
{
    private readonly IRepository<Location> _repository;
    private readonly IRepository<TimeSlot> _timeSlotRepository;

    /// <summary>Initializes a new instance of <see cref="LocationsController"/>.</summary>
    public LocationsController(IRepository<Location> repository, IRepository<TimeSlot> timeSlotRepository)
    {
        _repository = repository;
        _timeSlotRepository = timeSlotRepository;
    }

    /// <summary>Returns all clinic locations, optionally paged via <paramref name="page"/>/<paramref name="pageSize"/>.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LocationDto>>> GetAll(
        CancellationToken ct, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        IReadOnlyList<Location> locations;
        if (Paging.Normalize(page, pageSize) is { } paging)
        {
            Response.Headers[Paging.TotalCountHeader] = (await _repository.CountAsync(ct)).ToString();
            locations = await _repository.GetPagedAsync(paging.Skip, paging.Take, ct);
        }
        else
        {
            locations = await _repository.GetAllAsync(ct);
        }

        var allTimeSlots = await _timeSlotRepository.GetAllAsync(ct);
        var slotsByLocation = allTimeSlots.ToLookup(ts => ts.LocationId);

        return Ok(locations.Select(x => MapToDto(x, slotsByLocation[x.Id])).ToList());
    }

    /// <summary>Returns a single location by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<LocationDto>> GetById(int id, CancellationToken ct)
    {
        var location = await _repository.GetByIdAsync(id, ct);
        if (location is null) return NotFound();

        var timeSlots = await _timeSlotRepository.FindAsync(ts => ts.LocationId == id, ct);
        return Ok(MapToDto(location, timeSlots));
    }

    /// <summary>Creates a new clinic location with optional scheduling configuration.</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.AdminOrManager)]
    public async Task<ActionResult<LocationDto>> Create(CreateLocationRequest request, CancellationToken ct)
    {
        var location = new Location(request.Name, request.Address);
        location.UpdateAddress(request.Address, request.City, request.State, request.ZipCode);
        location.TimeZone = request.TimeZone;

        if (request.SlotDurationMinutes is { } slotMinutes)
        {
            try
            {
                location.SetSlotDuration(slotMinutes);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        var created = await _repository.AddAsync(location, ct);

        IReadOnlyList<TimeSlot> timeSlots = [];
        if (request.OperatingHours is { Count: > 0 } hours)
        {
            var result = await ReplaceOperatingHoursAsync(created, hours, ct);
            if (result.Error is not null) return BadRequest(result.Error);
            timeSlots = result.TimeSlots;
        }

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created, timeSlots));
    }

    /// <summary>Updates an existing location's details and scheduling configuration.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleNames.AdminOrManager)]
    public async Task<IActionResult> Update(int id, UpdateLocationRequest request, CancellationToken ct)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.UpdateDetails(request.Name, request.Address, request.City, request.State, request.ZipCode, request.TimeZone);

        if (request.SlotDurationMinutes is { } slotMinutes)
        {
            try
            {
                existing.SetSlotDuration(slotMinutes);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        await _repository.UpdateAsync(existing, ct);

        // Null leaves operating hours untouched; an empty list clears them (default schedule)
        if (request.OperatingHours is { } hours)
        {
            var result = await ReplaceOperatingHoursAsync(existing, hours, ct);
            if (result.Error is not null) return BadRequest(result.Error);
        }

        return NoContent();
    }

    /// <summary>Deletes a clinic location.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleNames.AdminOrManager)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var location = await _repository.GetByIdAsync(id, ct);
        if (location is null) return NotFound();

        await _repository.DeleteAsync(location, ct);
        return NoContent();
    }

    private async Task<(IReadOnlyList<TimeSlot> TimeSlots, string? Error)> ReplaceOperatingHoursAsync(
        Location location, List<OperatingHoursDto> hours, CancellationToken ct)
    {
        var newSlots = new List<TimeSlot>();
        foreach (var window in hours)
        {
            try
            {
                newSlots.Add(new TimeSlot(window.StartTime, window.EndTime, window.DayOfWeek, location));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                return ([], ex.Message);
            }
        }

        var existingSlots = await _timeSlotRepository.FindAsync(ts => ts.LocationId == location.Id, ct);
        if (existingSlots.Count > 0)
            await _timeSlotRepository.DeleteRangeAsync(existingSlots, ct);

        if (newSlots.Count > 0)
            await _timeSlotRepository.AddRangeAsync(newSlots, ct);

        return (newSlots, null);
    }

    private static LocationDto MapToDto(Location location, IEnumerable<TimeSlot>? timeSlots = null) => new()
    {
        Id = location.Id,
        Name = location.Name,
        Address = location.Address,
        City = location.City,
        State = location.State,
        ZipCode = location.ZipCode,
        TimeZone = location.TimeZone,
        DailyCapacity = location.DailyCapacity,
        SlotDurationMinutes = location.SlotDurationMinutes,
        OperatingHours = (timeSlots ?? [])
            .OrderBy(ts => ts.DayOfWeek)
            .ThenBy(ts => ts.StartTime)
            .Select(ts => new OperatingHoursDto
            {
                DayOfWeek = ts.DayOfWeek,
                StartTime = ts.StartTime,
                EndTime = ts.EndTime
            })
            .ToList(),
        CreatedAt = location.CreatedAt,
        UpdatedAt = location.UpdatedAt
    };
}
