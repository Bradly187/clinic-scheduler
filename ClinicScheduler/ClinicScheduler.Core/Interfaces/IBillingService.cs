using ClinicScheduler.Core.Entities;

namespace ClinicScheduler.Core.Interfaces;

/// <summary>
/// Manages the invoice lifecycle: creation, line item management, payment recording,
/// balance tracking, and invoice number generation.
/// </summary>
public interface IBillingService
{
    /// <summary>Creates a draft invoice for a completed appointment, auto-populating line items from the therapy type.</summary>
    Task<Invoice> CreateInvoiceForAppointmentAsync(Appointment appointment, CancellationToken ct = default);

    /// <summary>Creates a blank draft invoice for a patient.</summary>
    Task<Invoice> CreateInvoiceAsync(int patientId, DateTime dueDate, int? appointmentId = null, CancellationToken ct = default);

    /// <summary>Adds a line item to a draft invoice and recalculates totals.</summary>
    Task<InvoiceLineItem> AddLineItemAsync(int invoiceId, string description, int quantity, decimal unitPrice, string? billingCode = null, int? therapyTypeId = null, CancellationToken ct = default);

    /// <summary>Records a payment against an invoice and updates its status.</summary>
    Task<Payment> RecordPaymentAsync(int invoiceId, decimal amount, PaymentMethod method, string? stripePaymentIntentId = null, CancellationToken ct = default);

    /// <summary>Marks a sent/partial invoice as void.</summary>
    Task VoidInvoiceAsync(int invoiceId, CancellationToken ct = default);

    /// <summary>Sends a draft invoice (transitions to Sent status).</summary>
    Task SendInvoiceAsync(int invoiceId, CancellationToken ct = default);

    /// <summary>Gets the total outstanding balance for a patient across all unpaid invoices.</summary>
    Task<decimal> GetPatientBalanceAsync(int patientId, CancellationToken ct = default);

    /// <summary>Gets an invoice by ID with all related data (line items, payments).</summary>
    Task<Invoice?> GetInvoiceAsync(int invoiceId, CancellationToken ct = default);

    /// <summary>Gets all invoices for a patient.</summary>
    Task<IReadOnlyList<Invoice>> GetPatientInvoicesAsync(int patientId, CancellationToken ct = default);

    /// <summary>Generates the next sequential invoice number.</summary>
    Task<string> GenerateInvoiceNumberAsync(CancellationToken ct = default);
}
