using ClinicScheduler.Core.Entities;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Generates superbill data from an invoice and its associated entities.
/// </summary>
public sealed class SuperbillService
{
    private readonly IDbContextFactory<ClinicDbContext> _dbFactory;
    private readonly ILogger<SuperbillService> _logger;

    public SuperbillService(IDbContextFactory<ClinicDbContext> dbFactory, ILogger<SuperbillService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Creates a Superbill record from an invoice. Extracts diagnosis and procedure codes
    /// from the invoice line items and associated appointment.
    /// </summary>
    public async Task<Superbill> GenerateSuperbillAsync(int invoiceId, string[] diagnosisCodes, string[] procedureCodes, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var invoice = await db.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Appointment).ThenInclude(a => a!.Therapist)
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ArgumentException("Invoice not found.", nameof(invoiceId));

        var therapist = invoice.Appointment?.Therapist;
        if (therapist is null)
        {
            // Fall back: find a therapist from any appointment for this patient
            var recentAppt = await db.Appointments
                .Include(a => a.Therapist)
                .Where(a => a.PatientId == invoice.PatientId && a.Therapist != null)
                .OrderByDescending(a => a.StartTime)
                .FirstOrDefaultAsync(ct);
            therapist = recentAppt?.Therapist
                ?? throw new InvalidOperationException("No therapist found for this invoice. A superbill requires a rendering provider.");
        }

        var serviceDate = invoice.Appointment is not null
            ? DateOnly.FromDateTime(invoice.Appointment.StartTime)
            : DateOnly.FromDateTime(invoice.IssuedAt);

        var diagnosisJson = System.Text.Json.JsonSerializer.Serialize(diagnosisCodes);
        var procedureJson = System.Text.Json.JsonSerializer.Serialize(procedureCodes);

        var superbill = new Superbill(invoice, invoice.Patient, therapist, serviceDate, diagnosisJson, procedureJson);

        db.Superbills.Add(superbill);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Generated superbill {SuperbillId} for invoice {InvoiceId}", superbill.Id, invoiceId);
        return superbill;
    }

    /// <summary>Gets an existing superbill by ID with all includes.</summary>
    public async Task<Superbill?> GetSuperbillAsync(int superbillId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Superbills
            .Include(s => s.Invoice).ThenInclude(i => i.LineItems)
            .Include(s => s.Patient)
            .Include(s => s.Therapist)
            .FirstOrDefaultAsync(s => s.Id == superbillId, ct);
    }
}
