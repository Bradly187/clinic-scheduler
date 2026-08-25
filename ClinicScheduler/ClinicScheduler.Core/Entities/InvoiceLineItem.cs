namespace ClinicScheduler.Core.Entities;

/// <summary>
/// A single line item on an invoice representing a billable service or product.
/// </summary>
public class InvoiceLineItem
{
    public int Id { get; set; }

    public int InvoiceId { get; private set; }
    public Invoice Invoice { get; private set; } = null!;

    public string Description { get; private set; } = string.Empty;

    /// <summary>Number of units billed (typically 1 for a session).</summary>
    public int Quantity { get; private set; } = 1;

    /// <summary>Price per unit.</summary>
    public decimal UnitPrice { get; private set; }

    /// <summary>Total amount for this line (Quantity * UnitPrice).</summary>
    public decimal Amount { get; private set; }

    /// <summary>CPT or other billing code (e.g., "97110").</summary>
    public string? BillingCode { get; private set; }

    /// <summary>Optional reference to the therapy type that generated this line item.</summary>
    public int? TherapyTypeId { get; private set; }
    public TherapyType? TherapyType { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Private constructor for EF Core.</summary>
    private InvoiceLineItem() { }

    public InvoiceLineItem(Invoice invoice, string description, int quantity, decimal unitPrice, string? billingCode = null, TherapyType? therapyType = null)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");

        Invoice = invoice;
        InvoiceId = invoice.Id;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = quantity * unitPrice;
        BillingCode = billingCode;
        TherapyType = therapyType;
        TherapyTypeId = therapyType?.Id;
    }

    public void UpdateDetails(string description, int quantity, decimal unitPrice, string? billingCode = null)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");

        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Amount = quantity * unitPrice;
        BillingCode = billingCode;
    }
}
