namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents a billing invoice issued to a patient for services rendered.
/// </summary>
public class Invoice
{
    public int Id { get; set; }

    public int PatientId { get; private set; }
    public Patient Patient { get; private set; } = null!;

    public int? AppointmentId { get; private set; }
    public Appointment? Appointment { get; private set; }

    /// <summary>Auto-generated invoice number (e.g., "INV-2026-00042").</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    public DateTime IssuedAt { get; private set; } = DateTime.UtcNow;
    public DateTime DueDate { get; private set; }

    /// <summary>Sum of all line item amounts before tax.</summary>
    public decimal SubTotal { get; private set; }

    /// <summary>Tax amount applied to the subtotal.</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>Total amount due (SubTotal + TaxAmount).</summary>
    public decimal Total { get; private set; }

    /// <summary>Total amount paid so far across all payments.</summary>
    public decimal PaidAmount { get; private set; }

    /// <summary>Outstanding balance (Total - PaidAmount).</summary>
    public decimal Balance => Total - PaidAmount;

    public string? Notes { get; private set; }

    /// <summary>External Stripe invoice ID, if payment is processed through Stripe.</summary>
    public string? StripeInvoiceId { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InvoiceLineItem> LineItems { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];

    /// <summary>Private constructor for EF Core.</summary>
    private Invoice() { }

    public Invoice(Patient patient, string invoiceNumber, DateTime dueDate, Appointment? appointment = null)
    {
        Patient = patient;
        PatientId = patient.Id;
        InvoiceNumber = invoiceNumber;
        DueDate = dueDate;
        Appointment = appointment;
        AppointmentId = appointment?.Id;
        Status = InvoiceStatus.Draft;
        IssuedAt = DateTime.UtcNow;
    }

    /// <summary>Adds a line item and recalculates totals.</summary>
    public void AddLineItem(InvoiceLineItem item)
    {
        LineItems.Add(item);
        RecalculateTotals();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Removes a line item and recalculates totals. Only allowed on Draft invoices.</summary>
    public void RemoveLineItem(InvoiceLineItem item)
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException("Line items can only be removed from Draft invoices.");

        LineItems.Remove(item);
        RecalculateTotals();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Recalculates SubTotal and Total from line items.</summary>
    public void RecalculateTotals()
    {
        SubTotal = LineItems.Sum(li => li.Amount);
        Total = SubTotal + TaxAmount;
    }

    /// <summary>Sets the tax amount and recalculates total.</summary>
    public void SetTax(decimal taxAmount)
    {
        if (taxAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(taxAmount), "Tax cannot be negative.");
        TaxAmount = taxAmount;
        Total = SubTotal + TaxAmount;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the invoice as sent to the patient.</summary>
    public void Send()
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException($"Cannot send an invoice with status {Status}.");
        if (Total <= 0)
            throw new InvalidOperationException("Cannot send an invoice with zero or negative total.");
        Status = InvoiceStatus.Sent;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Records a payment amount and updates invoice status accordingly.</summary>
    public void RecordPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be positive.");
        if (Status is InvoiceStatus.Void or InvoiceStatus.Draft)
            throw new InvalidOperationException($"Cannot record payment on an invoice with status {Status}.");

        PaidAmount += amount;

        if (PaidAmount >= Total)
        {
            PaidAmount = Total; // Cap at total to avoid overpayment accounting issues
            Status = InvoiceStatus.Paid;
        }
        else
        {
            Status = InvoiceStatus.PartiallyPaid;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Voids the invoice, making it uncollectible.</summary>
    public void Void()
    {
        if (Status == InvoiceStatus.Paid)
            throw new InvalidOperationException("Cannot void a fully paid invoice. Issue a refund instead.");
        Status = InvoiceStatus.Void;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Marks the invoice as overdue (typically called by a background job).</summary>
    public void MarkOverdue()
    {
        if (Status is InvoiceStatus.Sent or InvoiceStatus.PartiallyPaid)
        {
            Status = InvoiceStatus.Overdue;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void SetNotes(string? notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetStripeInvoiceId(string? stripeInvoiceId)
    {
        StripeInvoiceId = stripeInvoiceId;
        UpdatedAt = DateTime.UtcNow;
    }
}
