namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents the lifecycle state of an invoice.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Invoice has been created but not yet sent to the patient.</summary>
    Draft,

    /// <summary>Invoice has been sent/presented to the patient for payment.</summary>
    Sent,

    /// <summary>Invoice has been fully paid.</summary>
    Paid,

    /// <summary>Invoice has received partial payment; a balance remains.</summary>
    PartiallyPaid,

    /// <summary>Invoice is past its due date and has not been fully paid.</summary>
    Overdue,

    /// <summary>Invoice has been voided and is no longer collectible.</summary>
    Void
}
