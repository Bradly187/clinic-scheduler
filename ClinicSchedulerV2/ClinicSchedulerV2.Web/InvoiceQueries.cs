using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Web;

/// <summary>Loads one invoice with what its pages show. A patient only ever gets their own sent invoices.</summary>
public static class InvoiceQueries
{
    public static async Task<(Invoice Invoice, InvoiceSummary Summary)?> LoadAsync(ClinicDb clinic, int invoiceId)
    {
        var actor = await clinic.ActorAsync();
        await using var db = await clinic.OpenAsync();
        var invoice = await db.Invoices
            .Include(i => i.Patient)
            .Include(i => i.Lines).ThenInclude(l => l.Appointment)
            .Include(i => i.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(i => i.InvoiceId == invoiceId);
        if (invoice is null) return null;
        if (actor.IsPatient && (invoice.PatientId != actor.PatientId || invoice.Status == InvoiceStatus.Draft)) return null;

        var summary = await db.InvoiceSummaries.SingleAsync(s => s.InvoiceId == invoiceId);
        return (invoice, summary);
    }
}
