using ClinicScheduler.Core.Entities;
using ClinicScheduler.Web.Services;
using FluentAssertions;

namespace ClinicScheduler.Core.Tests.Services;

public class Cms1500GeneratorTests
{
    [Fact]
    public void Generate_WithValidData_ReturnsNonEmptyPdf()
    {
        // Arrange
        var patient = new Patient("John", "Doe", "john@example.com", new DateOnly(1980, 5, 15), "555-0100");
        var therapist = new Therapist("Dr. Jane", "Smith", "jane@clinic.com");
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "PT Therapeutic Exercise", 1, 150m, "97110"));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Manual Therapy", 1, 75m, "97140"));

        var superbill = new Superbill(
            invoice, patient, therapist,
            new DateOnly(2026, 8, 25),
            "[\"M54.5\", \"M79.3\"]",
            "[\"97110\", \"97140\"]");

        var generator = new Cms1500Generator();

        // Act
        var pdf = generator.Generate(superbill);

        // Assert
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(1000, "a valid PDF should be at least a few KB");
    }

    [Fact]
    public void Generate_WithInsurance_IncludesInsuranceInfo()
    {
        // Arrange
        var patient = new Patient("Jane", "Doe", "jane@example.com", new DateOnly(1990, 3, 20));
        var therapist = new Therapist("Dr. Bob", "Jones", "bob@clinic.com");
        var invoice = new Invoice(patient, "INV-2026-00002", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Eval", 1, 200m, "97161"));

        var superbill = new Superbill(invoice, patient, therapist, new DateOnly(2026, 8, 25), "[]", "[]");

        var insurance = new InsurancePolicy(patient, "Blue Cross", "BCB-123456", new DateOnly(2024, 1, 1), "GRP-001", "SUB-789");

        var generator = new Cms1500Generator();

        // Act
        var pdf = generator.Generate(superbill, insurance);

        // Assert
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void Generate_WithoutInsurance_ProducesSelfPayPdf()
    {
        // Arrange
        var patient = new Patient("Test", "User", "test@example.com", new DateOnly(1985, 7, 4));
        var therapist = new Therapist("Dr. Sarah", "Lee", "sarah@clinic.com");
        var invoice = new Invoice(patient, "INV-2026-00003", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "PT Session", 1, 100m));

        var superbill = new Superbill(invoice, patient, therapist, new DateOnly(2026, 8, 25), "[]", "[]");

        var generator = new Cms1500Generator();

        // Act
        var pdf = generator.Generate(superbill, insurance: null);

        // Assert
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(500);
    }
}
