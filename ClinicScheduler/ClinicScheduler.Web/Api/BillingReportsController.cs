using System.Text;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Api;

[ApiController]
[Route("api/reports/billing")]
[Authorize(Roles = RoleNames.AdminOrManager)]
public class BillingReportsController : ControllerBase
{
    private readonly ClinicDbContext _db;

    public BillingReportsController(ClinicDbContext db) => _db = db;

    /// <summary>Exports billing data as CSV for the given date range.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var fromUtc = Paging.AsUtc(from ?? DateTime.UtcNow.AddDays(-30));
        var toUtc = Paging.AsUtc(to ?? DateTime.UtcNow).AddDays(1);

        var invoices = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Patient)
            .Include(i => i.Payments)
            .Include(i => i.LineItems)
            .Where(i => i.IssuedAt >= fromUtc && i.IssuedAt < toUtc)
            .OrderBy(i => i.IssuedAt)
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("InvoiceNumber,Patient,Status,IssuedDate,DueDate,SubTotal,Tax,Total,PaidAmount,Balance,PaymentMethods");

        foreach (var inv in invoices)
        {
            var methods = inv.Payments
                .Where(p => p.Status == PaymentStatus.Completed)
                .Select(p => p.Method.ToString())
                .Distinct();
            var methodStr = string.Join(";", methods);

            sb.AppendLine($"{inv.InvoiceNumber},\"{inv.Patient?.FullName}\",{inv.Status},{inv.IssuedAt:yyyy-MM-dd},{inv.DueDate:yyyy-MM-dd},{inv.SubTotal},{inv.TaxAmount},{inv.Total},{inv.PaidAmount},{inv.Balance},\"{methodStr}\"");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"billing-report-{fromUtc:yyyyMMdd}-{toUtc.AddDays(-1):yyyyMMdd}.csv");
    }

    /// <summary>Returns billing summary stats for the given date range.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<object>> GetSummary(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var fromUtc = Paging.AsUtc(from ?? DateTime.UtcNow.AddDays(-30));
        var toUtc = Paging.AsUtc(to ?? DateTime.UtcNow).AddDays(1);

        var invoices = await _db.Invoices
            .AsNoTracking()
            .Include(i => i.Payments)
            .Where(i => i.IssuedAt >= fromUtc && i.IssuedAt < toUtc)
            .ToListAsync(ct);

        var payments = invoices.SelectMany(i => i.Payments)
            .Where(p => p.Status == PaymentStatus.Completed)
            .ToList();

        return Ok(new
        {
            period = new { from = fromUtc, to = toUtc.AddDays(-1) },
            totalBilled = invoices.Sum(i => i.Total),
            totalCollected = payments.Sum(p => p.Amount),
            outstandingBalance = invoices.Where(i => i.Status != InvoiceStatus.Void && i.Status != InvoiceStatus.Paid).Sum(i => i.Balance),
            invoiceCount = invoices.Count,
            paidCount = invoices.Count(i => i.Status == InvoiceStatus.Paid),
            overdueCount = invoices.Count(i => i.Status == InvoiceStatus.Overdue),
            paymentMethodBreakdown = payments
                .GroupBy(p => p.Method)
                .Select(g => new { method = g.Key.ToString(), count = g.Count(), total = g.Sum(p => p.Amount) })
                .ToList()
        });
    }
}
