using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Services;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Contracts.Appointments;
using ClinicScheduler.Web.Contracts.TreatmentPlans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

/// <summary>Manages treatment plan resources.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.StaffOrAbove)]
public class TreatmentPlansController : ControllerBase
{
    private readonly ClinicDbContext _dbContext;
    private readonly TreatmentPlanScheduleService _scheduleService;

    /// <summary>Initializes a new instance of <see cref="TreatmentPlansController"/>.</summary>
    public TreatmentPlansController(ClinicDbContext dbContext, TreatmentPlanScheduleService scheduleService)
    {
        _dbContext = dbContext;
        _scheduleService = scheduleService;
    }

    /// <summary>
    /// Generates the plan's recurring appointment series: books the remaining sessions
    /// (TotalDays minus those already booked) at FrequencyPerWeek sessions per week,
    /// preferring the requested days and time. Returns the booked appointments along
    /// with a count of any sessions that could not be placed.
    /// </summary>
    [HttpPost("{id:int}/generate-appointments")]
    public async Task<ActionResult<GenerateAppointmentsResponse>> GenerateAppointments(
        int id, GenerateAppointmentsRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _scheduleService.GenerateAppointmentsAsync(
                id, request.RoomId, request.PreferredTime, request.PreferredDays, ct);

            return Ok(new GenerateAppointmentsResponse
            {
                SessionsRequested = result.SessionsRequested,
                SessionsBooked = result.SessionsBooked,
                SessionsUnbooked = result.SessionsUnbooked,
                Appointments = result.Created.Select(MapAppointmentToDto).ToList()
            });
        }
        catch (ArgumentException ex) when (ex.ParamName == "treatmentPlanId")
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    private static AppointmentDto MapAppointmentToDto(Appointment appointment) => new()
    {
        Id = appointment.Id,
        PatientId = appointment.PatientId,
        PatientName = appointment.Patient.FullName,
        TherapistId = appointment.TherapistId,
        TherapistName = appointment.Therapist.FullName,
        RoomId = appointment.RoomId,
        RoomName = appointment.Room.Name,
        TreatmentPlanId = appointment.TreatmentPlanId,
        StartTime = appointment.StartTime,
        EndTime = appointment.EndTime,
        Status = appointment.Status,
        HasConflict = appointment.HasConflict,
        Notes = appointment.Notes,
        CreatedAt = appointment.CreatedAt,
        UpdatedAt = appointment.UpdatedAt
    };

    /// <summary>Returns all treatment plans with their associated therapies, optionally paged via <paramref name="page"/>/<paramref name="pageSize"/>.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TreatmentPlanDto>>> GetAll(
        CancellationToken ct, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        IQueryable<TreatmentPlan> query = _dbContext.TreatmentPlans
            .AsNoTracking()
            .Include(x => x.Patient)
            .Include(x => x.Therapist)
            .Include(x => x.TreatmentPlanTherapies)
                .ThenInclude(x => x.TherapyType)
            .OrderByDescending(x => x.CreatedAt);

        if (Paging.Normalize(page, pageSize) is { } paging)
        {
            Response.Headers[Paging.TotalCountHeader] = (await query.CountAsync(ct)).ToString();
            query = query.Skip(paging.Skip).Take(paging.Take);
        }

        var plans = await query.ToListAsync(ct);
        return Ok(plans.Select(static x => MapToDto(x)).ToList());
    }

    /// <summary>Returns a single treatment plan by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TreatmentPlanDto>> GetById(int id, CancellationToken ct)
    {
        var plan = await _dbContext.TreatmentPlans
            .AsNoTracking()
            .Include(x => x.Patient)
            .Include(x => x.Therapist)
            .Include(x => x.TreatmentPlanTherapies)
                .ThenInclude(x => x.TherapyType)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return plan is null ? NotFound() : Ok(MapToDto(plan));
    }

    /// <summary>Creates a new treatment plan and associates it with the specified therapy types.</summary>
    [HttpPost]
    public async Task<ActionResult<TreatmentPlanDto>> Create(CreateTreatmentPlanRequest request, CancellationToken ct)
    {
        var normalizedIds = request.TherapyTypeIds.Distinct().ToList();

        var validationResult = await ValidateReferencesAsync(request.PatientId, request.TherapistId, normalizedIds, ct);
        if (validationResult is not null) return validationResult;

        var patient = await _dbContext.Patients.FindAsync([request.PatientId], ct);
        var therapist = await _dbContext.Therapists.FindAsync([request.TherapistId], ct);
        var therapyTypes = await _dbContext.TherapyTypes
            .Where(x => normalizedIds.Contains(x.Id))
            .ToListAsync(ct);

        TreatmentPlan plan;
        try
        {
            plan = new TreatmentPlan(patient!, therapist!, request.FrequencyPerWeek, request.TotalDays, request.StartDate);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        foreach (var therapyType in therapyTypes)
            plan.AddTherapy(therapyType);

        _dbContext.TreatmentPlans.Add(plan);
        await _dbContext.SaveChangesAsync(ct);

        var created = await _dbContext.TreatmentPlans
            .AsNoTracking()
            .Include(x => x.Patient)
            .Include(x => x.Therapist)
            .Include(x => x.TreatmentPlanTherapies)
                .ThenInclude(x => x.TherapyType)
            .FirstOrDefaultAsync(x => x.Id == plan.Id, ct);

        if (created is null)
            return StatusCode(500, "Treatment plan was created but could not be retrieved.");

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>Updates an existing treatment plan's schedule, therapist, and therapy types.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateTreatmentPlanRequest request, CancellationToken ct)
    {
        var existing = await _dbContext.TreatmentPlans
            .Include(x => x.TreatmentPlanTherapies)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (existing is null) return NotFound();

        var normalizedIds = request.TherapyTypeIds.Distinct().ToList();

        var validationResult = await ValidateReferencesAsync(request.PatientId, request.TherapistId, normalizedIds, ct);
        if (validationResult is not null) return validationResult;

        try
        {
            existing.UpdateSchedule(request.FrequencyPerWeek, request.TotalDays, request.StartDate);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        if (existing.TherapistId != request.TherapistId)
        {
            var newTherapist = await _dbContext.Therapists.FindAsync([request.TherapistId], ct);
            existing.ChangeTherapist(newTherapist!);
        }

        // Sync therapy types
        var requestedIds = normalizedIds.ToHashSet();
        var currentIds = existing.TreatmentPlanTherapies.Select(t => t.TherapyTypeId).ToHashSet();

        var toRemove = existing.TreatmentPlanTherapies
            .Where(t => !requestedIds.Contains(t.TherapyTypeId)).ToList();
        _dbContext.TreatmentPlanTherapies.RemoveRange(toRemove);

        var newIds = requestedIds.Except(currentIds).ToList();
        if (newIds.Count > 0)
        {
            var newTherapyTypes = await _dbContext.TherapyTypes
                .Where(x => newIds.Contains(x.Id))
                .ToListAsync(ct);
            foreach (var therapyType in newTherapyTypes)
                existing.AddTherapy(therapyType);
        }

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails { Detail = "The treatment plan was modified by another user. Reload and try again." });
        }

        return NoContent();
    }

    /// <summary>Deletes a treatment plan and its therapy associations.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var existing = await _dbContext.TreatmentPlans
            .Include(x => x.TreatmentPlanTherapies)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (existing is null) return NotFound();

        _dbContext.TreatmentPlanTherapies.RemoveRange(existing.TreatmentPlanTherapies);
        _dbContext.TreatmentPlans.Remove(existing);
        await _dbContext.SaveChangesAsync(ct);
        return NoContent();
    }

    private static TreatmentPlanDto MapToDto(TreatmentPlan plan) => new()
    {
        Id = plan.Id,
        PatientId = plan.PatientId,
        PatientName = plan.Patient?.FullName ?? "(unknown)",
        TherapistId = plan.TherapistId,
        TherapistName = plan.Therapist?.FullName ?? "(unknown)",
        FrequencyPerWeek = plan.FrequencyPerWeek,
        TotalDays = plan.TotalDays,
        StartDate = plan.StartDate,
        EndDate = DateTime.SpecifyKind(plan.EndDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
        Therapies = plan.TreatmentPlanTherapies
            .OrderBy(x => x.TherapyTypeId)
            .Select(x => new TreatmentPlanTherapyDto
            {
                TherapyTypeId = x.TherapyTypeId,
                TherapyTypeName = x.TherapyType?.Name ?? "(unknown)",
                Specialty = x.TherapyType?.Specialty ?? string.Empty,
                ColorCode = x.TherapyType?.ColorCode
            })
            .ToList(),
        CreatedAt = plan.CreatedAt,
        UpdatedAt = plan.UpdatedAt
    };

    private async Task<ActionResult?> ValidateReferencesAsync(
        int patientId, int therapistId,
        IReadOnlyCollection<int> therapyTypeIds,
        CancellationToken ct)
    {
        if (!await _dbContext.Patients.AnyAsync(x => x.Id == patientId, ct))
            return BadRequest("Invalid PatientId.");

        if (!await _dbContext.Therapists.AnyAsync(x => x.Id == therapistId, ct))
            return BadRequest("Invalid TherapistId.");

        if (therapyTypeIds.Count == 0)
            return BadRequest("At least one TherapyTypeId is required.");

        var validCount = await _dbContext.TherapyTypes.CountAsync(x => therapyTypeIds.Contains(x.Id), ct);
        if (validCount != therapyTypeIds.Count)
            return BadRequest("One or more TherapyTypeIds are invalid.");

        return null;
    }
}
