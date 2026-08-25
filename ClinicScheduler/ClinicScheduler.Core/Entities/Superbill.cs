namespace ClinicScheduler.Core.Entities;

/// <summary>
/// Represents a superbill/CMS-1500 document generated from an invoice for insurance filing.
/// </summary>
public class Superbill
{
    public int Id { get; set; }

    public int InvoiceId { get; private set; }
    public Invoice Invoice { get; private set; } = null!;

    public int PatientId { get; private set; }
    public Patient Patient { get; private set; } = null!;

    public int TherapistId { get; private set; }
    public Therapist Therapist { get; private set; } = null!;

    /// <summary>Date the services were rendered.</summary>
    public DateOnly ServiceDate { get; private set; }

    /// <summary>ICD-10 diagnosis codes (stored as JSON array, e.g., ["M54.5", "M79.3"]).</summary>
    public string DiagnosisCodes { get; private set; } = "[]";

    /// <summary>CPT procedure codes (stored as JSON array, e.g., ["97110", "97140"]).</summary>
    public string ProcedureCodes { get; private set; } = "[]";

    /// <summary>When this superbill was generated.</summary>
    public DateTime GeneratedAt { get; private set; } = DateTime.UtcNow;

    /// <summary>Path to the generated PDF file, if persisted to storage.</summary>
    public string? PdfPath { get; private set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Private constructor for EF Core.</summary>
    private Superbill() { }

    public Superbill(Invoice invoice, Patient patient, Therapist therapist, DateOnly serviceDate, string diagnosisCodes, string procedureCodes)
    {
        Invoice = invoice;
        InvoiceId = invoice.Id;
        Patient = patient;
        PatientId = patient.Id;
        Therapist = therapist;
        TherapistId = therapist.Id;
        ServiceDate = serviceDate;
        DiagnosisCodes = diagnosisCodes;
        ProcedureCodes = procedureCodes;
        GeneratedAt = DateTime.UtcNow;
    }

    public void SetPdfPath(string? pdfPath)
    {
        PdfPath = pdfPath;
    }
}
