using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicScheduler.Web.Tests.Fixtures;
using FluentAssertions;

namespace ClinicScheduler.Web.Tests.Api;

[Collection("WebApp")]
public class InvoicesApiTests : IAsyncLifetime
{
    private readonly WebAppFixture _fixture;

    public InvoicesApiTests(WebAppFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> SeedPatientAsync(string suffix = "") =>
        await SeedData.CreatePatientAsync(_fixture.Client, suffix);

    // ── Create Invoice ──

    [Fact]
    public async Task POST_CreateInvoice_Returns201()
    {
        var patientId = await SeedPatientAsync("inv1");

        var response = await _fixture.Client.PostAsJsonAsync("/api/invoices", new
        {
            PatientId = patientId,
            Notes = "Test invoice"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("invoiceNumber").GetString().Should().StartWith("INV-");
        doc.RootElement.GetProperty("status").GetString().Should().Be("Draft");
        doc.RootElement.GetProperty("patientId").GetInt32().Should().Be(patientId);
    }

    [Fact]
    public async Task POST_CreateInvoice_InvalidPatient_Returns400()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/invoices", new
        {
            PatientId = 99999
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Add Line Items ──

    [Fact]
    public async Task POST_AddLineItem_ReturnsLineItem()
    {
        var patientId = await SeedPatientAsync("li1");
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/invoices", new { PatientId = patientId });
        createResponse.EnsureSuccessStatusCode();
        var invoiceDoc = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var invoiceId = invoiceDoc.RootElement.GetProperty("id").GetInt32();

        var response = await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lineitems", new
        {
            Description = "Physical Therapy Session",
            Quantity = 1,
            UnitPrice = 150.00
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        doc.RootElement.GetProperty("description").GetString().Should().Be("Physical Therapy Session");
        doc.RootElement.GetProperty("amount").GetDecimal().Should().Be(150.00m);
    }

    // ── Send Invoice ──

    [Fact]
    public async Task POST_SendInvoice_TransitionsToSent()
    {
        var patientId = await SeedPatientAsync("send1");
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/invoices", new { PatientId = patientId });
        createResponse.EnsureSuccessStatusCode();
        var invoiceDoc = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var invoiceId = invoiceDoc.RootElement.GetProperty("id").GetInt32();

        // Add a line item so it has a positive total
        await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lineitems", new
        {
            Description = "PT Session",
            Quantity = 1,
            UnitPrice = 100.00
        });

        var sendResponse = await _fixture.Client.PostAsync($"/api/invoices/{invoiceId}/send", null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify status changed
        var getResponse = await _fixture.Client.GetAsync($"/api/invoices/{invoiceId}");
        var getDoc = await JsonDocument.ParseAsync(await getResponse.Content.ReadAsStreamAsync());
        getDoc.RootElement.GetProperty("status").GetString().Should().Be("Sent");
    }

    // ── Record Payment ──

    [Fact]
    public async Task POST_RecordPayment_FullAmount_MarksPaid()
    {
        var patientId = await SeedPatientAsync("pay1");
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/invoices", new { PatientId = patientId });
        createResponse.EnsureSuccessStatusCode();
        var invoiceDoc = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var invoiceId = invoiceDoc.RootElement.GetProperty("id").GetInt32();

        await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lineitems", new
        {
            Description = "Session",
            Quantity = 1,
            UnitPrice = 200.00
        });
        await _fixture.Client.PostAsync($"/api/invoices/{invoiceId}/send", null);

        var payResponse = await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            Amount = 200.00,
            Method = "Card",
            StripePaymentIntentId = "pi_test_123"
        });

        payResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payDoc = await JsonDocument.ParseAsync(await payResponse.Content.ReadAsStreamAsync());
        payDoc.RootElement.GetProperty("status").GetString().Should().Be("Completed");
        payDoc.RootElement.GetProperty("amount").GetDecimal().Should().Be(200.00m);

        // Verify invoice is now Paid
        var getResponse = await _fixture.Client.GetAsync($"/api/invoices/{invoiceId}");
        var getDoc = await JsonDocument.ParseAsync(await getResponse.Content.ReadAsStreamAsync());
        getDoc.RootElement.GetProperty("status").GetString().Should().Be("Paid");
        getDoc.RootElement.GetProperty("balance").GetDecimal().Should().Be(0m);
    }

    // ── Void Invoice ──

    [Fact]
    public async Task POST_VoidInvoice_TransitionsToVoid()
    {
        var patientId = await SeedPatientAsync("void1");
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/invoices", new { PatientId = patientId });
        createResponse.EnsureSuccessStatusCode();
        var invoiceDoc = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var invoiceId = invoiceDoc.RootElement.GetProperty("id").GetInt32();

        await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lineitems", new
        {
            Description = "Session",
            Quantity = 1,
            UnitPrice = 100.00
        });
        await _fixture.Client.PostAsync($"/api/invoices/{invoiceId}/send", null);

        var voidResponse = await _fixture.Client.PostAsync($"/api/invoices/{invoiceId}/void", null);
        voidResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _fixture.Client.GetAsync($"/api/invoices/{invoiceId}");
        var getDoc = await JsonDocument.ParseAsync(await getResponse.Content.ReadAsStreamAsync());
        getDoc.RootElement.GetProperty("status").GetString().Should().Be("Void");
    }

    // ── Get Balance ──

    [Fact]
    public async Task GET_PatientBalance_ReturnsCorrectAmount()
    {
        var patientId = await SeedPatientAsync("bal1");

        // Create and send invoice with $150 total
        var createResponse = await _fixture.Client.PostAsJsonAsync("/api/invoices", new { PatientId = patientId });
        createResponse.EnsureSuccessStatusCode();
        var doc = await JsonDocument.ParseAsync(await createResponse.Content.ReadAsStreamAsync());
        var invoiceId = doc.RootElement.GetProperty("id").GetInt32();

        await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lineitems", new
        {
            Description = "Session",
            Quantity = 1,
            UnitPrice = 150.00
        });
        await _fixture.Client.PostAsync($"/api/invoices/{invoiceId}/send", null);

        var balResponse = await _fixture.Client.GetAsync($"/api/invoices/balance/{patientId}");
        balResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var balDoc = await JsonDocument.ParseAsync(await balResponse.Content.ReadAsStreamAsync());
        balDoc.RootElement.GetProperty("outstandingBalance").GetDecimal().Should().Be(150.00m);
    }

    // ── List Invoices ──

    [Fact]
    public async Task GET_AllInvoices_Returns200()
    {
        var patientId = await SeedPatientAsync("list1");
        await _fixture.Client.PostAsJsonAsync("/api/invoices", new { PatientId = patientId });

        var response = await _fixture.Client.GetAsync("/api/invoices");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
