using System.Text.Json;
using ClinicScheduler.Core.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Generates a CMS-1500 style PDF from a Superbill record.
/// Uses QuestPDF for layout. The output approximates the standard CMS-1500 form
/// with patient info, insurance info, provider info, and service lines.
/// </summary>
public sealed class Cms1500Generator
{
    static Cms1500Generator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>
    /// Generates a CMS-1500 PDF as a byte array.
    /// </summary>
    public byte[] Generate(Superbill superbill, InsurancePolicy? insurance = null)
    {
        var diagnosisCodes = ParseJsonArray(superbill.DiagnosisCodes);
        var procedureCodes = ParseJsonArray(superbill.ProcedureCodes);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.5f, Unit.Inch);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text("CMS-1500 / HEALTH INSURANCE CLAIM FORM")
                        .Bold().FontSize(12).AlignCenter();
                    col.Item().PaddingBottom(10);
                });

                page.Content().Column(col =>
                {
                    // Section 1: Insurance Info
                    col.Item().Border(0.5f).Padding(8).Column(section =>
                    {
                        section.Item().Text("INSURANCE INFORMATION").Bold().FontSize(10);
                        section.Item().PaddingTop(4);

                        if (insurance is not null)
                        {
                            section.Item().Text($"Insurance: {insurance.ProviderName}");
                            section.Item().Text($"Policy #: {insurance.PolicyNumber}");
                            section.Item().Text($"Group #: {insurance.GroupNumber ?? "N/A"}");
                            section.Item().Text($"Subscriber ID: {insurance.SubscriberId ?? "N/A"}");
                        }
                        else
                        {
                            section.Item().Text("No insurance on file — patient self-pay");
                        }
                    });

                    col.Item().PaddingTop(8);

                    // Section 2: Patient Info
                    col.Item().Border(0.5f).Padding(8).Column(section =>
                    {
                        section.Item().Text("PATIENT INFORMATION").Bold().FontSize(10);
                        section.Item().PaddingTop(4);
                        section.Item().Text($"Name: {superbill.Patient.FullName}");
                        section.Item().Text($"DOB: {superbill.Patient.DateOfBirth:MM/dd/yyyy}");
                        section.Item().Text($"Address: (on file)");
                        section.Item().Text($"Phone: {superbill.Patient.Phone ?? "N/A"}");
                    });

                    col.Item().PaddingTop(8);

                    // Section 3: Provider Info
                    col.Item().Border(0.5f).Padding(8).Column(section =>
                    {
                        section.Item().Text("RENDERING PROVIDER").Bold().FontSize(10);
                        section.Item().PaddingTop(4);
                        section.Item().Text($"Name: {superbill.Therapist.FullName}");
                        section.Item().Text($"NPI: {superbill.Therapist.NpiNumber ?? "N/A"}");
                    });

                    col.Item().PaddingTop(8);

                    // Section 4: Diagnosis Codes
                    col.Item().Border(0.5f).Padding(8).Column(section =>
                    {
                        section.Item().Text("DIAGNOSIS CODES (ICD-10)").Bold().FontSize(10);
                        section.Item().PaddingTop(4);
                        for (int i = 0; i < diagnosisCodes.Length; i++)
                        {
                            section.Item().Text($"  {(char)('A' + i)}. {diagnosisCodes[i]}");
                        }
                        if (diagnosisCodes.Length == 0)
                            section.Item().Text("  (none specified)");
                    });

                    col.Item().PaddingTop(8);

                    // Section 5: Service Lines
                    col.Item().Border(0.5f).Padding(8).Column(section =>
                    {
                        section.Item().Text("SERVICE LINES").Bold().FontSize(10);
                        section.Item().PaddingTop(4);

                        // Header row
                        section.Item().Row(row =>
                        {
                            row.RelativeItem(2).Text("Date").Bold();
                            row.RelativeItem(2).Text("CPT Code").Bold();
                            row.RelativeItem(4).Text("Description").Bold();
                            row.RelativeItem(1).Text("Qty").Bold();
                            row.RelativeItem(2).Text("Charge").Bold();
                        });

                        section.Item().PaddingTop(2).LineHorizontal(0.5f);

                        if (superbill.Invoice?.LineItems is not null)
                        {
                            foreach (var item in superbill.Invoice.LineItems)
                            {
                                section.Item().PaddingTop(2).Row(row =>
                                {
                                    row.RelativeItem(2).Text(superbill.ServiceDate.ToString("MM/dd/yy"));
                                    row.RelativeItem(2).Text(item.BillingCode ?? "—");
                                    row.RelativeItem(4).Text(item.Description);
                                    row.RelativeItem(1).Text(item.Quantity.ToString());
                                    row.RelativeItem(2).Text(item.Amount.ToString("C"));
                                });
                            }
                        }

                        section.Item().PaddingTop(4).LineHorizontal(0.5f);
                        section.Item().PaddingTop(2).Row(row =>
                        {
                            row.RelativeItem(9).Text("TOTAL CHARGES:").Bold().AlignRight();
                            row.RelativeItem(2).Text((superbill.Invoice?.Total ?? 0).ToString("C")).Bold();
                        });
                    });

                    col.Item().PaddingTop(8);

                    // Section 6: Procedure codes reference
                    if (procedureCodes.Length > 0)
                    {
                        col.Item().Border(0.5f).Padding(8).Column(section =>
                        {
                            section.Item().Text("PROCEDURE CODES (CPT)").Bold().FontSize(10);
                            section.Item().PaddingTop(4);
                            section.Item().Text(string.Join(", ", procedureCodes));
                        });
                    }
                });

                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(10).LineHorizontal(0.5f);
                    col.Item().PaddingTop(4).Row(row =>
                    {
                        row.RelativeItem().Text($"Generated: {DateTime.UtcNow:MM/dd/yyyy HH:mm} UTC").FontSize(7);
                        row.RelativeItem().Text("ClinicScheduler CMS-1500").FontSize(7).AlignRight();
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string[] ParseJsonArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
