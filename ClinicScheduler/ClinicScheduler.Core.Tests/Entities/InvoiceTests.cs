using ClinicScheduler.Core.Entities;
using FluentAssertions;

namespace ClinicScheduler.Core.Tests.Entities;

public class InvoiceTests
{
    private Patient CreateTestPatient() =>
        new("John", "Doe", "john@example.com", new DateOnly(1980, 1, 1));

    private Invoice CreateTestInvoice(Patient? patient = null) =>
        new(patient ?? CreateTestPatient(), "INV-2026-00001", DateTime.UtcNow.AddDays(30));

    [Fact]
    public void Constructor_ShouldCreateDraftInvoice()
    {
        var patient = CreateTestPatient();
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));

        invoice.Patient.Should().Be(patient);
        invoice.InvoiceNumber.Should().Be("INV-2026-00001");
        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.SubTotal.Should().Be(0);
        invoice.Total.Should().Be(0);
        invoice.PaidAmount.Should().Be(0);
        invoice.Balance.Should().Be(0);
    }

    [Fact]
    public void AddLineItem_ShouldRecalculateTotals()
    {
        var invoice = CreateTestInvoice();
        var item = new InvoiceLineItem(invoice, "Physical Therapy Session", 1, 150m);

        invoice.AddLineItem(item);

        invoice.SubTotal.Should().Be(150m);
        invoice.Total.Should().Be(150m);
        invoice.LineItems.Should().HaveCount(1);
    }

    [Fact]
    public void AddLineItem_MultipleItems_ShouldSumCorrectly()
    {
        var invoice = CreateTestInvoice();
        var item1 = new InvoiceLineItem(invoice, "PT Session", 1, 150m);
        var item2 = new InvoiceLineItem(invoice, "Exercise Therapy", 2, 75m);

        invoice.AddLineItem(item1);
        invoice.AddLineItem(item2);

        invoice.SubTotal.Should().Be(300m); // 150 + (2*75)
        invoice.Total.Should().Be(300m);
    }

    [Fact]
    public void SetTax_ShouldUpdateTotal()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));

        invoice.SetTax(8.50m);

        invoice.TaxAmount.Should().Be(8.50m);
        invoice.Total.Should().Be(108.50m);
    }

    [Fact]
    public void SetTax_NegativeAmount_ShouldThrow()
    {
        var invoice = CreateTestInvoice();

        var act = () => invoice.SetTax(-5m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Send_FromDraft_ShouldChangeStatusToSent()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));

        invoice.Send();

        invoice.Status.Should().Be(InvoiceStatus.Sent);
    }

    [Fact]
    public void Send_WithZeroTotal_ShouldThrow()
    {
        var invoice = CreateTestInvoice();

        var act = () => invoice.Send();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*zero or negative*");
    }

    [Fact]
    public void Send_FromNonDraft_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();

        var act = () => invoice.Send();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordPayment_PartialPayment_ShouldUpdateStatus()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 200m));
        invoice.Send();

        invoice.RecordPayment(100m);

        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        invoice.PaidAmount.Should().Be(100m);
        invoice.Balance.Should().Be(100m);
    }

    [Fact]
    public void RecordPayment_FullPayment_ShouldMarkPaid()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 150m));
        invoice.Send();

        invoice.RecordPayment(150m);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.PaidAmount.Should().Be(150m);
        invoice.Balance.Should().Be(0m);
    }

    [Fact]
    public void RecordPayment_Overpayment_ShouldCapAtTotal()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();

        invoice.RecordPayment(150m);

        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.PaidAmount.Should().Be(100m);
        invoice.Balance.Should().Be(0m);
    }

    [Fact]
    public void RecordPayment_OnDraftInvoice_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));

        var act = () => invoice.RecordPayment(50m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RecordPayment_ZeroAmount_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();

        var act = () => invoice.RecordPayment(0m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Void_ShouldChangeStatus()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();

        invoice.Void();

        invoice.Status.Should().Be(InvoiceStatus.Void);
    }

    [Fact]
    public void Void_PaidInvoice_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();
        invoice.RecordPayment(100m);

        var act = () => invoice.Void();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*refund*");
    }

    [Fact]
    public void MarkOverdue_FromSent_ShouldChangeStatus()
    {
        var invoice = CreateTestInvoice();
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();

        invoice.MarkOverdue();

        invoice.Status.Should().Be(InvoiceStatus.Overdue);
    }

    [Fact]
    public void MarkOverdue_FromDraft_ShouldNotChangeStatus()
    {
        var invoice = CreateTestInvoice();

        invoice.MarkOverdue();

        invoice.Status.Should().Be(InvoiceStatus.Draft);
    }

    [Fact]
    public void RemoveLineItem_OnDraftInvoice_ShouldRecalculate()
    {
        var invoice = CreateTestInvoice();
        var item1 = new InvoiceLineItem(invoice, "Session 1", 1, 100m);
        var item2 = new InvoiceLineItem(invoice, "Session 2", 1, 50m);
        invoice.AddLineItem(item1);
        invoice.AddLineItem(item2);

        invoice.RemoveLineItem(item1);

        invoice.SubTotal.Should().Be(50m);
        invoice.LineItems.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveLineItem_OnSentInvoice_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        var item = new InvoiceLineItem(invoice, "Session", 1, 100m);
        invoice.AddLineItem(item);
        invoice.Send();

        var act = () => invoice.RemoveLineItem(item);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Draft*");
    }
}
