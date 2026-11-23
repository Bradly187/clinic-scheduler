using ClinicSchedulerV2.Data.Model;
using Microsoft.EntityFrameworkCore;

namespace ClinicSchedulerV2.Data;

/// <summary>
/// Invoices, lines, payments and insurance. Totals, balances, status changes and what may be
/// billed are all decided by 05_billing.sql; this class only issues the writes.
/// </summary>
public sealed class BillingService(ClinicDb clinic) : DbService(clinic)
{
    public Task<WriteResult> CreateInvoiceAsync(int patientId, string? notes) => WriteAsync(async (db, actor) =>
    {
        var invoice = new Invoice
        {
            PatientId = patientId,
            CreatedBy = actor.EmployeeId ?? 0, // the foreign key to staff rejects anyone else
            Notes = SchedulingService.Clean(notes),
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.InvoiceId;
    });

    public Task<WriteResult> AddLineAsync(
        int invoiceId, int? appointmentId, string description, string? billingCode, short quantity, decimal unitPrice) =>
        WriteAsync(async (db, _) =>
        {
            var line = new InvoiceLine
            {
                InvoiceId = invoiceId,
                AppointmentId = appointmentId,
                Description = description.Trim(),
                BillingCode = SchedulingService.Clean(billingCode),
                Quantity = quantity,
                UnitPrice = unitPrice,
            };
            db.InvoiceLines.Add(line);
            await db.SaveChangesAsync();
            return line.LineId;
        });

    public Task<WriteResult> RemoveLineAsync(int lineId) => WriteAsync(async (db, _) =>
    {
        await db.InvoiceLines.Where(l => l.LineId == lineId).ExecuteDeleteAsync();
        return lineId;
    });

    /// <summary>Draft to Sent. The database stamps the issue date and a due date 30 days out.</summary>
    public Task<WriteResult> SendAsync(int invoiceId) => SetStatusAsync(invoiceId, InvoiceStatus.Sent, null);

    public Task<WriteResult> VoidAsync(int invoiceId, string reason) => SetStatusAsync(invoiceId, InvoiceStatus.Void, reason);

    public Task<WriteResult> DeleteDraftAsync(int invoiceId) => WriteAsync(async (db, _) =>
    {
        await db.Invoices.Where(i => i.InvoiceId == invoiceId).ExecuteDeleteAsync();
        return invoiceId;
    });

    /// <summary>Records a payment; paying the balance in full marks the invoice Paid (trigger).</summary>
    public Task<WriteResult> RecordPaymentAsync(int invoiceId, decimal amount, PaymentMethod method, string? reference) =>
        WriteAsync(async (db, actor) =>
        {
            var payment = new Payment
            {
                InvoiceId = invoiceId,
                Amount = amount,
                Method = method,
                Reference = SchedulingService.Clean(reference),
                RecordedBy = actor.EmployeeId ?? 0,
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync();
            return payment.PaymentId;
        });

    public Task<WriteResult> AddPolicyAsync(InsurancePolicy policy) => WriteAsync(async (db, _) =>
    {
        db.InsurancePolicies.Add(policy);
        await db.SaveChangesAsync();
        return policy.PolicyId;
    });

    public Task<WriteResult> DeactivatePolicyAsync(int policyId) => WriteAsync(async (db, _) =>
    {
        await db.InsurancePolicies.Where(p => p.PolicyId == policyId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsActive, false));
        return policyId;
    });

    private Task<WriteResult> SetStatusAsync(int invoiceId, InvoiceStatus status, string? reason) => WriteAsync(async (db, _) =>
    {
        await db.Invoices.Where(i => i.InvoiceId == invoiceId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(i => i.Status, status)
                .SetProperty(i => i.VoidReason, i => reason ?? i.VoidReason));
        return invoiceId;
    });
}
