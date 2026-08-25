using ClinicScheduler.Core.Entities;
using FluentAssertions;

namespace ClinicScheduler.Core.Tests.Entities;

public class InvoiceLineItemTests
{
    private Invoice CreateTestInvoice()
    {
        var patient = new Patient("John", "Doe", "john@example.com", new DateOnly(1980, 1, 1));
        return new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
    }

    [Fact]
    public void Constructor_ShouldCalculateAmount()
    {
        var invoice = CreateTestInvoice();

        var item = new InvoiceLineItem(invoice, "PT Session", 2, 75m, "97110");

        item.Description.Should().Be("PT Session");
        item.Quantity.Should().Be(2);
        item.UnitPrice.Should().Be(75m);
        item.Amount.Should().Be(150m);
        item.BillingCode.Should().Be("97110");
    }

    [Fact]
    public void Constructor_ZeroQuantity_ShouldThrow()
    {
        var invoice = CreateTestInvoice();

        var act = () => new InvoiceLineItem(invoice, "Session", 0, 100m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeQuantity_ShouldThrow()
    {
        var invoice = CreateTestInvoice();

        var act = () => new InvoiceLineItem(invoice, "Session", -1, 100m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeUnitPrice_ShouldThrow()
    {
        var invoice = CreateTestInvoice();

        var act = () => new InvoiceLineItem(invoice, "Session", 1, -50m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_ZeroUnitPrice_ShouldSucceed()
    {
        var invoice = CreateTestInvoice();

        var item = new InvoiceLineItem(invoice, "Free consultation", 1, 0m);

        item.Amount.Should().Be(0m);
    }

    [Fact]
    public void UpdateDetails_ShouldRecalculateAmount()
    {
        var invoice = CreateTestInvoice();
        var item = new InvoiceLineItem(invoice, "Old description", 1, 100m);

        item.UpdateDetails("New description", 3, 50m, "97140");

        item.Description.Should().Be("New description");
        item.Quantity.Should().Be(3);
        item.UnitPrice.Should().Be(50m);
        item.Amount.Should().Be(150m);
        item.BillingCode.Should().Be("97140");
    }

    [Fact]
    public void Constructor_WithTherapyType_ShouldSetReference()
    {
        var invoice = CreateTestInvoice();
        var therapyType = new TherapyType("Physical Therapy", "PT desc", "PT", null, 150m);

        var item = new InvoiceLineItem(invoice, "Physical Therapy Session", 1, 150m, "97110", therapyType);

        item.TherapyType.Should().Be(therapyType);
    }
}
