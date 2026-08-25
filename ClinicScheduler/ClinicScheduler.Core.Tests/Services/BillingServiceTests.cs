using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Core.Services;
using FluentAssertions;
using Moq;

namespace ClinicScheduler.Core.Tests.Services;

public class BillingServiceTests
{
    private readonly Mock<IRepository<Invoice>> _invoiceRepo = new();
    private readonly Mock<IRepository<InvoiceLineItem>> _lineItemRepo = new();
    private readonly Mock<IRepository<Payment>> _paymentRepo = new();
    private readonly Mock<IRepository<Patient>> _patientRepo = new();
    private readonly Mock<IRepository<Appointment>> _appointmentRepo = new();
    private readonly Mock<IRepository<TherapyType>> _therapyTypeRepo = new();

    private BillingService CreateService() =>
        new(_invoiceRepo.Object, _lineItemRepo.Object, _paymentRepo.Object,
            _patientRepo.Object, _appointmentRepo.Object, _therapyTypeRepo.Object,
            NullLogger<BillingService>.Instance);

    private static Patient MakePatient()
    {
        var p = new Patient("John", "Doe", "john@example.com", new DateOnly(1985, 1, 1));
        // Use reflection to set Id for testing
        typeof(Patient).GetProperty("Id")!.SetValue(p, 1);
        return p;
    }

    private static TherapyType MakeTherapyType() =>
        new("Physical Therapy", "PT session", "PT", "#4A90D9", 150m);

    private static Appointment MakeAppointment(Patient patient, TherapyType? therapyType = null)
    {
        var therapist = new Therapist("Dr. Smith", "smith@clinic.com", "Physical Therapy");
        var room = new Room("Room 101", 1, new Location("Main Clinic", "123 Main St"));
        var appt = new Appointment(patient, therapist, room, DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(30), therapyType);
        typeof(Appointment).GetProperty("Id")!.SetValue(appt, 42);
        return appt;
    }

    private void SetupInvoiceRepo()
    {
        _invoiceRepo.Setup(r => r.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice i, CancellationToken _) => i);
        _invoiceRepo.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ─── CreateInvoiceForAppointmentAsync ───

    [Fact]
    public async Task CreateInvoiceForAppointment_ShouldCreateDraftWithLineItem()
    {
        // Arrange
        var patient = MakePatient();
        var therapyType = MakeTherapyType();
        var appointment = MakeAppointment(patient, therapyType);
        SetupInvoiceRepo();

        _therapyTypeRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(therapyType);

        var service = CreateService();

        // Act
        var invoice = await service.CreateInvoiceForAppointmentAsync(appointment);

        // Assert
        invoice.Should().NotBeNull();
        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.PatientId.Should().Be(patient.Id);
        invoice.InvoiceNumber.Should().StartWith("INV-");
        invoice.LineItems.Should().HaveCount(1);
        invoice.SubTotal.Should().Be(150m);
        invoice.Total.Should().Be(150m);

        _invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateInvoiceForAppointment_WithoutTherapyType_ShouldCreateEmptyInvoice()
    {
        // Arrange
        var patient = MakePatient();
        var appointment = MakeAppointment(patient);
        SetupInvoiceRepo();

        var service = CreateService();

        // Act
        var invoice = await service.CreateInvoiceForAppointmentAsync(appointment);

        // Assert
        invoice.LineItems.Should().BeEmpty();
        invoice.SubTotal.Should().Be(0);
    }

    // ─── CreateInvoiceAsync ───

    [Fact]
    public async Task CreateInvoice_ShouldCreateDraftForPatient()
    {
        // Arrange
        var patient = MakePatient();
        _patientRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);
        SetupInvoiceRepo();

        var service = CreateService();

        // Act
        var invoice = await service.CreateInvoiceAsync(1, DateTime.UtcNow.AddDays(30));

        // Assert
        invoice.Status.Should().Be(InvoiceStatus.Draft);
        invoice.PatientId.Should().Be(1);
    }

    [Fact]
    public async Task CreateInvoice_PatientNotFound_ShouldThrow()
    {
        // Arrange
        _patientRepo.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
        SetupInvoiceRepo();

        var service = CreateService();

        // Act
        var act = () => service.CreateInvoiceAsync(999, DateTime.UtcNow.AddDays(30));

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Patient not found*");
    }

    // ─── AddLineItemAsync ───

    [Fact]
    public async Task AddLineItem_ToDraftInvoice_ShouldSucceed()
    {
        // Arrange
        var patient = MakePatient();
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        typeof(Invoice).GetProperty("Id")!.SetValue(invoice, 1);

        _invoiceRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepo.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        var item = await service.AddLineItemAsync(1, "PT Session", 2, 75m, "97110");

        // Assert
        item.Description.Should().Be("PT Session");
        item.Amount.Should().Be(150m);
        invoice.SubTotal.Should().Be(150m);
    }

    [Fact]
    public async Task AddLineItem_ToSentInvoice_ShouldThrow()
    {
        // Arrange
        var patient = MakePatient();
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();
        typeof(Invoice).GetProperty("Id")!.SetValue(invoice, 1);

        _invoiceRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);

        var service = CreateService();

        // Act
        var act = () => service.AddLineItemAsync(1, "Extra", 1, 50m);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Draft*");
    }

    // ─── RecordPaymentAsync ───

    [Fact]
    public async Task RecordPayment_FullAmount_ShouldMarkInvoicePaid()
    {
        // Arrange
        var patient = MakePatient();
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 200m));
        invoice.Send();
        typeof(Invoice).GetProperty("Id")!.SetValue(invoice, 1);

        _invoiceRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepo.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _paymentRepo.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        var service = CreateService();

        // Act
        var payment = await service.RecordPaymentAsync(1, 200m, PaymentMethod.Card, "pi_test_123");

        // Assert
        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.Amount.Should().Be(200m);
        payment.StripePaymentIntentId.Should().Be("pi_test_123");
        invoice.Status.Should().Be(InvoiceStatus.Paid);
        invoice.PaidAmount.Should().Be(200m);
    }

    [Fact]
    public async Task RecordPayment_PartialAmount_ShouldMarkPartiallyPaid()
    {
        // Arrange
        var patient = MakePatient();
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 200m));
        invoice.Send();
        typeof(Invoice).GetProperty("Id")!.SetValue(invoice, 1);

        _invoiceRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepo.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _paymentRepo.Setup(r => r.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Payment p, CancellationToken _) => p);

        var service = CreateService();

        // Act
        var payment = await service.RecordPaymentAsync(1, 100m, PaymentMethod.Cash);

        // Assert
        invoice.Status.Should().Be(InvoiceStatus.PartiallyPaid);
        invoice.Balance.Should().Be(100m);
    }

    // ─── VoidInvoiceAsync ───

    [Fact]
    public async Task VoidInvoice_SentInvoice_ShouldSucceed()
    {
        // Arrange
        var patient = MakePatient();
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        invoice.Send();
        typeof(Invoice).GetProperty("Id")!.SetValue(invoice, 1);

        _invoiceRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepo.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.VoidInvoiceAsync(1);

        // Assert
        invoice.Status.Should().Be(InvoiceStatus.Void);
    }

    // ─── SendInvoiceAsync ───

    [Fact]
    public async Task SendInvoice_DraftWithTotal_ShouldSucceed()
    {
        // Arrange
        var patient = MakePatient();
        var invoice = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        invoice.AddLineItem(new InvoiceLineItem(invoice, "Session", 1, 100m));
        typeof(Invoice).GetProperty("Id")!.SetValue(invoice, 1);

        _invoiceRepo.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoice);
        _invoiceRepo.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService();

        // Act
        await service.SendInvoiceAsync(1);

        // Assert
        invoice.Status.Should().Be(InvoiceStatus.Sent);
    }

    // ─── GetPatientBalanceAsync ───

    [Fact]
    public async Task GetPatientBalance_ShouldSumOutstandingInvoices()
    {
        // Arrange
        var patient = MakePatient();

        var inv1 = new Invoice(patient, "INV-2026-00001", DateTime.UtcNow.AddDays(30));
        inv1.AddLineItem(new InvoiceLineItem(inv1, "Session 1", 1, 100m));
        inv1.Send();

        var inv2 = new Invoice(patient, "INV-2026-00002", DateTime.UtcNow.AddDays(30));
        inv2.AddLineItem(new InvoiceLineItem(inv2, "Session 2", 1, 150m));
        inv2.Send();
        inv2.RecordPayment(50m); // partially paid, balance = 100

        _invoiceRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Invoice, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Invoice> { inv1, inv2 });

        var service = CreateService();

        // Act
        var balance = await service.GetPatientBalanceAsync(1);

        // Assert
        balance.Should().Be(200m); // 100 + 100
    }

    // ─── GenerateInvoiceNumberAsync ───

    [Fact]
    public async Task GenerateInvoiceNumber_ShouldReturnSequentialNumber()
    {
        // Arrange
        _invoiceRepo.Setup(r => r.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(41);

        var service = CreateService();

        // Act
        var number = await service.GenerateInvoiceNumberAsync();

        // Assert
        number.Should().Be($"INV-{DateTime.UtcNow.Year}-00042");
    }

    [Fact]
    public async Task GenerateInvoiceNumber_FirstInvoice_ShouldBeOne()
    {
        // Arrange
        _invoiceRepo.Setup(r => r.CountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var service = CreateService();

        // Act
        var number = await service.GenerateInvoiceNumberAsync();

        // Assert
        number.Should().Be($"INV-{DateTime.UtcNow.Year}-00001");
    }
}
