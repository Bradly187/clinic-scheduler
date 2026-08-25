using System.ComponentModel.DataAnnotations;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.StaffOrAbove)]
public class SuperbillsController : ControllerBase
{
    private readonly SuperbillService _superbillService;
    private readonly Cms1500Generator _cms1500Generator;
    private readonly ClinicDbContext _db;

    public SuperbillsController(SuperbillService superbillService, Cms1500Generator cms1500Generator, ClinicDbContext db)
    {
        _superbillService = superbillService;
        _cms1500Generator = cms1500Generator;
        _db = db;
    }

    /// <summary>Generates a superbill for an invoice.</summary>
    [HttpPost]
    public async Task<ActionResult<SuperbillDto>> Generate(GenerateSuperbillRequest request, CancellationToken ct)
    {
        try
        {
            var superbill = await _superbillService.GenerateSuperbillAsync(
                request.InvoiceId, request.DiagnosisCodes ?? [], request.ProcedureCodes ?? [], ct);

            return Ok(new SuperbillDto
            {
                Id = superbill.Id,
                InvoiceId = superbill.InvoiceId,
                PatientName = superbill.Patient.FullName,
                TherapistName = superbill.Therapist.FullName,
                ServiceDate = superbill.ServiceDate,
                DiagnosisCodes = request.DiagnosisCodes ?? [],
                ProcedureCodes = request.ProcedureCodes ?? [],
                GeneratedAt = superbill.GeneratedAt
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>Downloads a CMS-1500 PDF for an invoice.</summary>
    [HttpGet("{invoiceId:int}/cms1500")]
    public async Task<IActionResult> DownloadCms1500(int invoiceId, CancellationToken ct)
    {
        var superbill = await _db.Superbills
            .Include(s => s.Invoice).ThenInclude(i => i.LineItems)
            .Include(s => s.Patient)
            .Include(s => s.Therapist)
            .FirstOrDefaultAsync(s => s.InvoiceId == invoiceId, ct);

        if (superbill is null)
            return NotFound(new ProblemDetails { Detail = "No superbill found for this invoice. Generate one first." });

        // Look up insurance for the patient
        var insurance = await _db.InsurancePolicies
            .AsNoTracking()
            .Where(ip => ip.PatientId == superbill.PatientId && ip.IsActive)
            .OrderByDescending(ip => ip.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        var pdf = _cms1500Generator.Generate(superbill, insurance);

        return File(pdf, "application/pdf", $"CMS1500-{superbill.Invoice.InvoiceNumber}.pdf");
    }

    /// <summary>Downloads a superbill PDF for an invoice (same as CMS-1500 but named differently).</summary>
    [HttpGet("{invoiceId:int}/pdf")]
    public async Task<IActionResult> DownloadSuperbill(int invoiceId, CancellationToken ct)
    {
        return await DownloadCms1500(invoiceId, ct);
    }
}

public sealed class GenerateSuperbillRequest
{
    [Required]
    [Range(1, int.MaxValue)]
    public int InvoiceId { get; init; }

    /// <summary>ICD-10 diagnosis codes (e.g., ["M54.5", "M79.3"]).</summary>
    public string[]? DiagnosisCodes { get; init; }

    /// <summary>CPT procedure codes (e.g., ["97110", "97140"]).</summary>
    public string[]? ProcedureCodes { get; init; }
}

public sealed class SuperbillDto
{
    public int Id { get; init; }
    public int InvoiceId { get; init; }
    public string PatientName { get; init; } = string.Empty;
    public string TherapistName { get; init; } = string.Empty;
    public DateOnly ServiceDate { get; init; }
    public string[] DiagnosisCodes { get; init; } = [];
    public string[] ProcedureCodes { get; init; } = [];
    public DateTime GeneratedAt { get; init; }
}
