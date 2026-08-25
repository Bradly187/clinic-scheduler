using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Contracts.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly ClinicDbContext _db;
    private readonly IBillingService _billingService;

    public InvoicesController(ClinicDbContext db, IBillingService billingService)
    {
        _db = db;
        _billingService = billingService;
    }

    /// <summary>Lists invoices. Staff sees all; patients see only their own.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> GetAll(
        CancellationToken ct,
        [FromQuery] int? patientId = null,
        [FromQuery] InvoiceStatus? status = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var isStaff = User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.ClinicManager)
                   || User.IsInRole(RoleNames.Staff) || User.IsInRole(RoleNames.Therapist);

        var query = _db.Invoices
            .AsNoTracking()
            .Include(i => i.Patient)
            .Include(i => i.LineItems).ThenInclude(li => li.TherapyType)
            .Include(i => i.Payments)
            .AsQueryable();

        if (!isStaff)
        {
            // Patients see only their own invoices
            var email = User.Identity?.Name;
            query = query.Where(i => i.Patient.Email == email);
        }
        else if (patientId.HasValue)
        {
            query = query.Where(i => i.PatientId == patientId.Value);
        }

        if (status.HasValue)
            query = query.Where(i => i.Status == status.Value);

        query = query.OrderByDescending(i => i.IssuedAt);

        if (Paging.Normalize(page, pageSize) is { } paging)
        {
            Response.Headers[Paging.TotalCountHeader] = (await query.CountAsync(ct)).ToString();
            query = query.Skip(paging.Skip).Take(paging.Take);
        }

        var invoices = await query.ToListAsync(ct);
        return Ok(invoices.Select(MapToDto).ToList());
    }

    /// <summary>Gets a single invoice by ID with full detail.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvoiceDto>> GetById(int id, CancellationToken ct)
    {
        var invoice = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Patient)
            .Include(i => i.LineItems).ThenInclude(li => li.TherapyType)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (invoice is null) return NotFound();

        // Patients can only see their own
        if (!IsStaff() && invoice.Patient.Email != User.Identity?.Name)
            return Forbid();

        return Ok(MapToDto(invoice));
    }

    /// <summary>Creates a new draft invoice.</summary>
    [HttpPost]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult<InvoiceDto>> Create(CreateInvoiceRequest request, CancellationToken ct)
    {
        var dueDate = request.DueDate ?? DateTime.UtcNow.AddDays(30);

        try
        {
            var invoice = await _billingService.CreateInvoiceAsync(request.PatientId, dueDate, request.AppointmentId, ct);

            if (!string.IsNullOrWhiteSpace(request.Notes))
                invoice.SetNotes(request.Notes);

            // Re-fetch with includes for the DTO
            var created = await _db.Invoices
                .AsNoTracking()
                .Include(i => i.Patient)
                .Include(i => i.LineItems)
                .Include(i => i.Payments)
                .FirstAsync(i => i.Id == invoice.Id, ct);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>Adds a line item to a draft invoice.</summary>
    [HttpPost("{id:int}/lineitems")]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult<InvoiceLineItemDto>> AddLineItem(int id, AddLineItemRequest request, CancellationToken ct)
    {
        try
        {
            var item = await _billingService.AddLineItemAsync(
                id, request.Description, request.Quantity, request.UnitPrice, request.BillingCode, request.TherapyTypeId, ct);

            return Ok(new InvoiceLineItemDto
            {
                Id = item.Id,
                Description = item.Description,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = item.Amount,
                BillingCode = item.BillingCode,
                TherapyTypeId = item.TherapyTypeId,
                TherapyTypeName = item.TherapyType?.Name
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>Sends a draft invoice (transitions to Sent).</summary>
    [HttpPost("{id:int}/send")]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult> Send(int id, CancellationToken ct)
    {
        try
        {
            await _billingService.SendInvoiceAsync(id, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>Voids an invoice.</summary>
    [HttpPost("{id:int}/void")]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult> Void(int id, CancellationToken ct)
    {
        try
        {
            await _billingService.VoidInvoiceAsync(id, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>Records a payment against an invoice.</summary>
    [HttpPost("{id:int}/payments")]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult<PaymentDto>> RecordPayment(int id, RecordPaymentRequest request, CancellationToken ct)
    {
        try
        {
            var payment = await _billingService.RecordPaymentAsync(
                id, request.Amount, request.Method, request.StripePaymentIntentId, ct);

            if (!string.IsNullOrWhiteSpace(request.Notes))
                payment.SetNotes(request.Notes);

            return Ok(new PaymentDto
            {
                Id = payment.Id,
                Amount = payment.Amount,
                Method = payment.Method,
                Status = payment.Status,
                StripePaymentIntentId = payment.StripePaymentIntentId,
                PaidAt = payment.PaidAt,
                Notes = payment.Notes,
                CreatedAt = payment.CreatedAt
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>Gets the outstanding balance for a patient.</summary>
    [HttpGet("balance/{patientId:int}")]
    [Authorize(Roles = RoleNames.StaffOrAbove)]
    public async Task<ActionResult<object>> GetBalance(int patientId, CancellationToken ct)
    {
        var patient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null) return NotFound();

        var balance = await _billingService.GetPatientBalanceAsync(patientId, ct);
        return Ok(new { patientId, patientName = patient.FullName, outstandingBalance = balance });
    }

    private bool IsStaff() =>
        User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.ClinicManager)
        || User.IsInRole(RoleNames.Staff) || User.IsInRole(RoleNames.Therapist);

    private static InvoiceDto MapToDto(Invoice i) => new()
    {
        Id = i.Id,
        PatientId = i.PatientId,
        PatientName = i.Patient?.FullName ?? "Unknown",
        AppointmentId = i.AppointmentId,
        InvoiceNumber = i.InvoiceNumber,
        Status = i.Status,
        IssuedAt = i.IssuedAt,
        DueDate = i.DueDate,
        SubTotal = i.SubTotal,
        TaxAmount = i.TaxAmount,
        Total = i.Total,
        PaidAmount = i.PaidAmount,
        Balance = i.Balance,
        Notes = i.Notes,
        LineItems = i.LineItems.Select(li => new InvoiceLineItemDto
        {
            Id = li.Id,
            Description = li.Description,
            Quantity = li.Quantity,
            UnitPrice = li.UnitPrice,
            Amount = li.Amount,
            BillingCode = li.BillingCode,
            TherapyTypeId = li.TherapyTypeId,
            TherapyTypeName = li.TherapyType?.Name
        }).ToList(),
        Payments = i.Payments.Select(p => new PaymentDto
        {
            Id = p.Id,
            Amount = p.Amount,
            Method = p.Method,
            Status = p.Status,
            StripePaymentIntentId = p.StripePaymentIntentId,
            PaidAt = p.PaidAt,
            RefundedAt = p.RefundedAt,
            Notes = p.Notes,
            CreatedAt = p.CreatedAt
        }).ToList()
    };
}
