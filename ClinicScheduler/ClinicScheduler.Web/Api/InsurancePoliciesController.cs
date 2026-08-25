using ClinicScheduler.Core.Entities;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Contracts.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.StaffOrAbove)]
public class InsurancePoliciesController : ControllerBase
{
    private readonly ClinicDbContext _db;

    public InsurancePoliciesController(ClinicDbContext db) => _db = db;

    /// <summary>Lists insurance policies, optionally filtered by patient.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InsurancePolicyDto>>> GetAll(
        CancellationToken ct, [FromQuery] int? patientId = null, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var query = _db.InsurancePolicies
            .AsNoTracking()
            .Include(ip => ip.Patient)
            .AsQueryable();

        if (patientId.HasValue)
            query = query.Where(ip => ip.PatientId == patientId.Value);

        query = query.OrderByDescending(ip => ip.EffectiveDate);

        if (Paging.Normalize(page, pageSize) is { } paging)
        {
            Response.Headers[Paging.TotalCountHeader] = (await query.CountAsync(ct)).ToString();
            query = query.Skip(paging.Skip).Take(paging.Take);
        }

        var policies = await query.ToListAsync(ct);
        return Ok(policies.Select(MapToDto).ToList());
    }

    /// <summary>Gets a single insurance policy.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<InsurancePolicyDto>> GetById(int id, CancellationToken ct)
    {
        var policy = await _db.InsurancePolicies
            .AsNoTracking()
            .Include(ip => ip.Patient)
            .FirstOrDefaultAsync(ip => ip.Id == id, ct);

        return policy is null ? NotFound() : Ok(MapToDto(policy));
    }

    /// <summary>Creates a new insurance policy for a patient.</summary>
    [HttpPost]
    public async Task<ActionResult<InsurancePolicyDto>> Create(CreateInsurancePolicyRequest request, CancellationToken ct)
    {
        var patient = await _db.Patients.FindAsync([request.PatientId], ct);
        if (patient is null)
            return BadRequest(new ProblemDetails { Detail = "Patient not found." });

        var policy = new InsurancePolicy(
            patient,
            request.ProviderName,
            request.PolicyNumber,
            request.EffectiveDate,
            request.GroupNumber,
            request.SubscriberId);

        if (request.TerminationDate.HasValue)
            policy.UpdateDetails(request.ProviderName, request.PolicyNumber, request.EffectiveDate, request.GroupNumber, request.SubscriberId, request.TerminationDate);

        if (!string.IsNullOrWhiteSpace(request.Notes))
            policy.SetNotes(request.Notes);

        _db.InsurancePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);

        var created = await _db.InsurancePolicies
            .AsNoTracking()
            .Include(ip => ip.Patient)
            .FirstAsync(ip => ip.Id == policy.Id, ct);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>Updates an existing insurance policy.</summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<InsurancePolicyDto>> Update(int id, UpdateInsurancePolicyRequest request, CancellationToken ct)
    {
        var policy = await _db.InsurancePolicies
            .Include(ip => ip.Patient)
            .FirstOrDefaultAsync(ip => ip.Id == id, ct);

        if (policy is null) return NotFound();

        policy.UpdateDetails(
            request.ProviderName,
            request.PolicyNumber,
            request.EffectiveDate,
            request.GroupNumber,
            request.SubscriberId,
            request.TerminationDate);

        if (request.Notes is not null)
            policy.SetNotes(request.Notes);

        await _db.SaveChangesAsync(ct);
        return Ok(MapToDto(policy));
    }

    /// <summary>Deactivates an insurance policy.</summary>
    [HttpPost("{id:int}/deactivate")]
    public async Task<ActionResult> Deactivate(int id, CancellationToken ct)
    {
        var policy = await _db.InsurancePolicies.FindAsync([id], ct);
        if (policy is null) return NotFound();

        policy.Deactivate();
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Deletes an insurance policy.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleNames.AdminOrManager)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        var policy = await _db.InsurancePolicies.FindAsync([id], ct);
        if (policy is null) return NotFound();

        _db.InsurancePolicies.Remove(policy);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static InsurancePolicyDto MapToDto(InsurancePolicy ip) => new()
    {
        Id = ip.Id,
        PatientId = ip.PatientId,
        PatientName = ip.Patient?.FullName ?? "Unknown",
        ProviderName = ip.ProviderName,
        PolicyNumber = ip.PolicyNumber,
        GroupNumber = ip.GroupNumber,
        SubscriberId = ip.SubscriberId,
        EffectiveDate = ip.EffectiveDate,
        TerminationDate = ip.TerminationDate,
        IsActive = ip.IsActive,
        Notes = ip.Notes
    };
}
