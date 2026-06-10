using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Web.Contracts.Patients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

/// <summary>Manages patient resources.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PatientsController : ControllerBase
{
    private readonly IRepository<Patient> _repository;
    private readonly IAuditLogger _auditLogger;

    /// <summary>Initializes a new instance of <see cref="PatientsController"/>.</summary>
    public PatientsController(IRepository<Patient> repository, IAuditLogger auditLogger)
    {
        _repository = repository;
        _auditLogger = auditLogger;
    }

    /// <summary>Returns all patients, optionally paged via <paramref name="page"/>/<paramref name="pageSize"/>.</summary>
    [HttpGet]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult<IReadOnlyList<PatientDto>>> GetAll(
        CancellationToken ct, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        if (Paging.Normalize(page, pageSize) is { } paging)
        {
            Response.Headers[Paging.TotalCountHeader] = (await _repository.CountAsync(ct)).ToString();
            var paged = await _repository.GetPagedAsync(paging.Skip, paging.Take, ct);
            return Ok(paged.Select(static p => MapToDto(p)).ToList());
        }

        var patients = await _repository.GetAllAsync(ct);
        return Ok(patients.Select(static p => MapToDto(p)).ToList());
    }

    /// <summary>Returns a single patient by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PatientDto>> GetById(int id, CancellationToken ct)
    {
        var patient = await _repository.GetByIdAsync(id, ct);
        if (patient is null) return NotFound();

        // Staff_Or_Above get full access; Patient role gets owner-access only
        if (User.IsInRole(RoleNames.Patient)
            && !User.IsInRole(RoleNames.Admin)
            && !User.IsInRole(RoleNames.ClinicManager)
            && !User.IsInRole(RoleNames.Staff)
            && !User.IsInRole(RoleNames.Therapist))
        {
            var userEmail = User.Identity?.Name;
            if (!string.Equals(patient.Email, userEmail, StringComparison.OrdinalIgnoreCase))
                return Forbid();
        }

        // HIPAA access logging: record who viewed this patient record
        await _auditLogger.LogAccessAsync(nameof(Patient), patient.Id.ToString(), "Patient record viewed via API", ct);

        return Ok(MapToDto(patient));
    }

    /// <summary>Creates a new patient record.</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult<PatientDto>> Create(CreatePatientRequest request, CancellationToken ct)
    {
        var patient = new Patient(request.FirstName, request.LastName, request.Email, request.DateOfBirth, request.Phone);
        var created = await _repository.AddAsync(patient, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>Updates a patient's personal and contact information.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<IActionResult> Update(int id, UpdatePatientRequest request, CancellationToken ct)
    {
        var existing = await _repository.GetByIdAsync(id, ct);
        if (existing is null) return NotFound();

        existing.UpdateDetails(request.FirstName, request.LastName, request.DateOfBirth);
        existing.UpdateContactInfo(request.Email, request.Phone);

        try
        {
            await _repository.UpdateAsync(existing, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails { Detail = "The record was modified by another user. Reload and try again." });
        }

        return NoContent();
    }

    /// <summary>Deletes a patient record.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var patient = await _repository.GetByIdAsync(id, ct);
        if (patient is null) return NotFound();

        await _repository.DeleteAsync(patient, ct);
        return NoContent();
    }

    private static PatientDto MapToDto(Patient patient) => new()
    {
        Id = patient.Id,
        FirstName = patient.FirstName,
        LastName = patient.LastName,
        Email = patient.Email,
        Phone = patient.Phone,
        DateOfBirth = patient.DateOfBirth,
        CreatedAt = patient.CreatedAt,
        UpdatedAt = patient.UpdatedAt
    };
}
