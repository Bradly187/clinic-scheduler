using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace ClinicScheduler.Core.Services;

/// <summary>
/// Manages the full invoice lifecycle: creation from appointments, line item management,
/// payment recording, balance calculation, and invoice number generation.
/// </summary>
public class BillingService : IBillingService
{
    private readonly IRepository<Invoice> _invoiceRepository;
    private readonly IRepository<InvoiceLineItem> _lineItemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<Patient> _patientRepository;
    private readonly IRepository<Appointment> _appointmentRepository;
    private readonly IRepository<TherapyType> _therapyTypeRepository;
    private readonly ILogger<BillingService> _logger;

    private static readonly ActivitySource _activitySource = new("ClinicScheduler.BusinessLogic");
    private static readonly Meter _meter = new("ClinicScheduler.BusinessLogic");

    private static readonly Counter<int> _invoicesCreated = _meter.CreateCounter<int>("clinic.billing.invoices_created", description: "Number of invoices created");
    private static readonly Counter<int> _paymentsRecorded = _meter.CreateCounter<int>("clinic.billing.payments_recorded", description: "Number of payments recorded");
    private static readonly Counter<int> _invoicesVoided = _meter.CreateCounter<int>("clinic.billing.invoices_voided", description: "Number of invoices voided");

    public BillingService(
        IRepository<Invoice> invoiceRepository,
        IRepository<InvoiceLineItem> lineItemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<Patient> patientRepository,
        IRepository<Appointment> appointmentRepository,
        IRepository<TherapyType> therapyTypeRepository,
        ILogger<BillingService> logger)
    {
        _invoiceRepository = invoiceRepository;
        _lineItemRepository = lineItemRepository;
        _paymentRepository = paymentRepository;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _therapyTypeRepository = therapyTypeRepository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Invoice> CreateInvoiceForAppointmentAsync(Appointment appointment, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("CreateInvoiceForAppointment");
        activity?.SetTag("billing.appointmentId", appointment.Id);
        activity?.SetTag("billing.patientId", appointment.PatientId);

        var patient = appointment.Patient
            ?? await _patientRepository.GetByIdAsync(appointment.PatientId, ct)
            ?? throw new ArgumentException("Patient not found for the given appointment.");

        var invoiceNumber = await GenerateInvoiceNumberAsync(ct);
        var dueDate = DateTime.UtcNow.AddDays(30);
        var invoice = new Invoice(patient, invoiceNumber, dueDate, appointment);

        // Auto-create line item from therapy type if available
        if (appointment.TherapyTypeId.HasValue)
        {
            var therapyType = appointment.TherapyType
                ?? await _therapyTypeRepository.GetByIdAsync(appointment.TherapyTypeId.Value, ct);

            if (therapyType is not null)
            {
                var rate = therapyType.DefaultRate ?? 0m;
                var item = new InvoiceLineItem(
                    invoice,
                    $"{therapyType.Name} - Session",
                    1,
                    rate,
                    billingCode: null,
                    therapyType);
                invoice.AddLineItem(item);
            }
        }

        await _invoiceRepository.AddAsync(invoice, ct);
        _invoicesCreated.Add(1);
        _logger.LogInformation("Created invoice {InvoiceNumber} for appointment {AppointmentId}", invoiceNumber, appointment.Id);

        return invoice;
    }

    /// <inheritdoc/>
    public async Task<Invoice> CreateInvoiceAsync(int patientId, DateTime dueDate, int? appointmentId = null, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("CreateInvoice");
        activity?.SetTag("billing.patientId", patientId);

        var patient = await _patientRepository.GetByIdAsync(patientId, ct)
            ?? throw new ArgumentException("Patient not found.", nameof(patientId));

        Appointment? appointment = null;
        if (appointmentId.HasValue)
        {
            appointment = await _appointmentRepository.GetByIdAsync(appointmentId.Value, ct);
        }

        var invoiceNumber = await GenerateInvoiceNumberAsync(ct);
        var invoice = new Invoice(patient, invoiceNumber, dueDate, appointment);

        await _invoiceRepository.AddAsync(invoice, ct);
        _invoicesCreated.Add(1);
        _logger.LogInformation("Created invoice {InvoiceNumber} for patient {PatientId}", invoiceNumber, patientId);

        return invoice;
    }

    /// <inheritdoc/>
    public async Task<InvoiceLineItem> AddLineItemAsync(int invoiceId, string description, int quantity, decimal unitPrice, string? billingCode = null, int? therapyTypeId = null, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("AddLineItem");

        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, ct)
            ?? throw new ArgumentException("Invoice not found.", nameof(invoiceId));

        if (invoice.Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Line items can only be added to Draft invoices.");

        TherapyType? therapyType = null;
        if (therapyTypeId.HasValue)
        {
            therapyType = await _therapyTypeRepository.GetByIdAsync(therapyTypeId.Value, ct);
        }

        var item = new InvoiceLineItem(invoice, description, quantity, unitPrice, billingCode, therapyType);
        invoice.AddLineItem(item);
        await _invoiceRepository.UpdateAsync(invoice, ct);

        _logger.LogInformation("Added line item to invoice {InvoiceId}: {Description} x{Quantity} @ {UnitPrice}", invoiceId, description, quantity, unitPrice);
        return item;
    }

    /// <inheritdoc/>
    public async Task<Payment> RecordPaymentAsync(int invoiceId, decimal amount, PaymentMethod method, string? stripePaymentIntentId = null, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("RecordPayment");
        activity?.SetTag("billing.invoiceId", invoiceId);
        activity?.SetTag("billing.amount", amount);
        activity?.SetTag("billing.method", method.ToString());

        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, ct)
            ?? throw new ArgumentException("Invoice not found.", nameof(invoiceId));

        var payment = new Payment(invoice, amount, method, stripePaymentIntentId);
        payment.MarkCompleted();

        invoice.RecordPayment(amount);

        await _paymentRepository.AddAsync(payment, ct);
        await _invoiceRepository.UpdateAsync(invoice, ct);

        _paymentsRecorded.Add(1);
        _logger.LogInformation("Recorded {Method} payment of {Amount:C} on invoice {InvoiceId}. New status: {Status}",
            method, amount, invoiceId, invoice.Status);

        return payment;
    }

    /// <inheritdoc/>
    public async Task VoidInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("VoidInvoice");

        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, ct)
            ?? throw new ArgumentException("Invoice not found.", nameof(invoiceId));

        invoice.Void();
        await _invoiceRepository.UpdateAsync(invoice, ct);

        _invoicesVoided.Add(1);
        _logger.LogInformation("Voided invoice {InvoiceId}", invoiceId);
    }

    /// <inheritdoc/>
    public async Task SendInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        using var activity = _activitySource.StartActivity("SendInvoice");

        var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, ct)
            ?? throw new ArgumentException("Invoice not found.", nameof(invoiceId));

        invoice.Send();
        await _invoiceRepository.UpdateAsync(invoice, ct);

        _logger.LogInformation("Sent invoice {InvoiceId} ({InvoiceNumber})", invoiceId, invoice.InvoiceNumber);
    }

    /// <inheritdoc/>
    public async Task<decimal> GetPatientBalanceAsync(int patientId, CancellationToken ct = default)
    {
        var invoices = await _invoiceRepository.FindAsync(
            i => i.PatientId == patientId
                 && i.Status != InvoiceStatus.Draft
                 && i.Status != InvoiceStatus.Void
                 && i.Status != InvoiceStatus.Paid,
            ct);

        return invoices.Sum(i => i.Balance);
    }

    /// <inheritdoc/>
    public async Task<Invoice?> GetInvoiceAsync(int invoiceId, CancellationToken ct = default)
    {
        return await _invoiceRepository.GetByIdAsync(invoiceId, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Invoice>> GetPatientInvoicesAsync(int patientId, CancellationToken ct = default)
    {
        return await _invoiceRepository.FindAsync(i => i.PatientId == patientId, ct);
    }

    /// <inheritdoc/>
    public async Task<string> GenerateInvoiceNumberAsync(CancellationToken ct = default)
    {
        var year = DateTime.UtcNow.Year;
        var count = await _invoiceRepository.CountAsync(ct);
        var sequence = count + 1;
        return $"INV-{year}-{sequence:D5}";
    }
}
