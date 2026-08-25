using ClinicScheduler.Core.Entities;
using FluentAssertions;

namespace ClinicScheduler.Core.Tests.Entities;

public class PaymentTests
{
    private Invoice CreateTestInvoice()
    {
        var patient = new Patient("Jane", "Doe", "jane@example.com", new DateOnly(1990, 5, 15));
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "PT Session", 1, 150m));
        invoice.Send();
        return invoice;
    }

    [Fact]
    public void Constructor_ShouldCreatePendingPayment()
    {
        var invoice = CreateTestInvoice();

        var payment = new Payment(invoice, 150m, PaymentMethod.Card, "pi_test_123");

        payment.Amount.Should().Be(150m);
        payment.Method.Should().Be(PaymentMethod.Card);
        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.StripePaymentIntentId.Should().Be("pi_test_123");
        payment.PaidAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ZeroAmount_ShouldThrow()
    {
        var invoice = CreateTestInvoice();

        var act = () => new Payment(invoice, 0m, PaymentMethod.Cash);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeAmount_ShouldThrow()
    {
        var invoice = CreateTestInvoice();

        var act = () => new Payment(invoice, -50m, PaymentMethod.Cash);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkCompleted_ShouldSetPaidAt()
    {
        var invoice = CreateTestInvoice();
        var payment = new Payment(invoice, 100m, PaymentMethod.Card);

        payment.MarkCompleted();

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.PaidAt.Should().NotBeNull();
        payment.PaidAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MarkCompleted_WhenNotPending_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        var payment = new Payment(invoice, 100m, PaymentMethod.Card);
        payment.MarkCompleted();

        var act = () => payment.MarkCompleted();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkFailed_ShouldUpdateStatus()
    {
        var invoice = CreateTestInvoice();
        var payment = new Payment(invoice, 100m, PaymentMethod.Card);

        payment.MarkFailed();

        payment.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public void MarkFailed_WhenNotPending_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        var payment = new Payment(invoice, 100m, PaymentMethod.Card);
        payment.MarkCompleted();

        var act = () => payment.MarkFailed();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Refund_CompletedPayment_ShouldSetRefundedAt()
    {
        var invoice = CreateTestInvoice();
        var payment = new Payment(invoice, 100m, PaymentMethod.Card);
        payment.MarkCompleted();

        payment.Refund();

        payment.Status.Should().Be(PaymentStatus.Refunded);
        payment.RefundedAt.Should().NotBeNull();
    }

    [Fact]
    public void Refund_PendingPayment_ShouldThrow()
    {
        var invoice = CreateTestInvoice();
        var payment = new Payment(invoice, 100m, PaymentMethod.Card);

        var act = () => payment.Refund();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*completed*");
    }

    [Theory]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.Insurance)]
    [InlineData(PaymentMethod.Other)]
    public void Constructor_AllPaymentMethods_ShouldSucceed(PaymentMethod method)
    {
        var invoice = CreateTestInvoice();

        var payment = new Payment(invoice, 50m, method);

        payment.Method.Should().Be(method);
    }
}
