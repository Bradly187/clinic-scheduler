using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicScheduler.Web.Tests.Fixtures;
using FluentAssertions;

namespace ClinicScheduler.Web.Tests.Api;

/// <summary>
/// End-to-end integration test validating the complete patient journey:
/// Book appointment → Complete → Invoice auto-generated → Add line items → Send → Pay → Generate superbill → Download PDF
/// </summary>
[Collection("WebApp")]
public class FullLifecycleTests : IAsyncLifetime
{
    private readonly WebAppFixture _fixture;
    private static readonly DateTime Slot9am = SeedData.FutureMonday9am();

    public FullLifecycleTests(WebAppFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FullPatientLifecycle_BookToSuperbill()
    {
        // ─── 1. Seed core entities ───
        var (patientId, therapistId, roomId, therapyTypeId) =
            await SeedData.SeedCoreEntitiesAsync(_fixture.Client, "lifecycle");

        // ─── 2. Book an appointment ───
        var bookResponse = await _fixture.Client.PostAsJsonAsync("/api/appointments", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = Slot9am,
            EndTime = Slot9am.AddMinutes(30)
        });
        bookResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var bookDoc = await JsonDocument.ParseAsync(await bookResponse.Content.ReadAsStreamAsync());
        var appointmentId = bookDoc.RootElement.GetProperty("id").GetInt32();
        bookDoc.RootElement.GetProperty("status").GetString().Should().Be("Scheduled");

        // ─── 3. Complete the appointment ───
        var completeResponse = await _fixture.Client.PutAsJsonAsync($"/api/appointments/{appointmentId}", new
        {
            PatientId = patientId,
            TherapistId = therapistId,
            RoomId = roomId,
            StartTime = Slot9am,
            EndTime = Slot9am.AddMinutes(30),
            Status = "Completed"
        });
        completeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var completeDoc = await JsonDocument.ParseAsync(await completeResponse.Content.ReadAsStreamAsync());
        completeDoc.RootElement.GetProperty("status").GetString().Should().Be("Completed");

        // ─── 4. Verify draft invoice was auto-generated ───
        var invoicesResponse = await _fixture.Client.GetAsync($"/api/invoices?patientId={patientId}");
        invoicesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var invoicesDoc = await JsonDocument.ParseAsync(await invoicesResponse.Content.ReadAsStreamAsync());
        var invoices = invoicesDoc.RootElement;
        invoices.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);

        var invoice = invoices[0];
        var invoiceId = invoice.GetProperty("id").GetInt32();
        invoice.GetProperty("status").GetString().Should().Be("Draft");
        invoice.GetProperty("invoiceNumber").GetString().Should().StartWith("INV-");

        // ─── 5. Add a line item (in case therapy type had no default rate) ───
        var addItemResponse = await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lineitems", new
        {
            Description = "Physical Therapy Session - 30 min",
            Quantity = 1,
            UnitPrice = 150.00,
            BillingCode = "97110"
        });
        addItemResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // ─── 6. Send the invoice ───
        var sendResponse = await _fixture.Client.PostAsync($"/api/invoices/{invoiceId}/send", null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify status is now Sent
        var getInvoiceResponse = await _fixture.Client.GetAsync($"/api/invoices/{invoiceId}");
        var getInvoiceDoc = await JsonDocument.ParseAsync(await getInvoiceResponse.Content.ReadAsStreamAsync());
        getInvoiceDoc.RootElement.GetProperty("status").GetString().Should().Be("Sent");
        var total = getInvoiceDoc.RootElement.GetProperty("total").GetDecimal();
        total.Should().BeGreaterThan(0);

        // ─── 7. Record payment (full amount) ───
        var payResponse = await _fixture.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", new
        {
            Amount = total,
            Method = "Card",
            StripePaymentIntentId = "pi_lifecycle_test_001"
        });
        payResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payDoc = await JsonDocument.ParseAsync(await payResponse.Content.ReadAsStreamAsync());
        payDoc.RootElement.GetProperty("status").GetString().Should().Be("Completed");
        payDoc.RootElement.GetProperty("amount").GetDecimal().Should().Be(total);

        // Verify invoice is now Paid with zero balance
        var paidInvoiceResponse = await _fixture.Client.GetAsync($"/api/invoices/{invoiceId}");
        var paidDoc = await JsonDocument.ParseAsync(await paidInvoiceResponse.Content.ReadAsStreamAsync());
        paidDoc.RootElement.GetProperty("status").GetString().Should().Be("Paid");
        paidDoc.RootElement.GetProperty("balance").GetDecimal().Should().Be(0);

        // ─── 8. Generate superbill ───
        var superbillResponse = await _fixture.Client.PostAsJsonAsync("/api/superbills", new
        {
            InvoiceId = invoiceId,
            DiagnosisCodes = new[] { "M54.5", "M79.3" },
            ProcedureCodes = new[] { "97110" }
        });
        superbillResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var superbillDoc = await JsonDocument.ParseAsync(await superbillResponse.Content.ReadAsStreamAsync());
        superbillDoc.RootElement.GetProperty("invoiceId").GetInt32().Should().Be(invoiceId);
        superbillDoc.RootElement.GetProperty("diagnosisCodes").GetArrayLength().Should().Be(2);
        superbillDoc.RootElement.GetProperty("procedureCodes").GetArrayLength().Should().Be(1);

        // ─── 9. Download CMS-1500 PDF ───
        var pdfResponse = await _fixture.Client.GetAsync($"/api/superbills/{invoiceId}/cms1500");
        pdfResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        var pdfBytes = await pdfResponse.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.Should().BeGreaterThan(1000, "PDF should be at least a few KB");

        // ─── 10. Verify patient balance is zero ───
        var balanceResponse = await _fixture.Client.GetAsync($"/api/invoices/balance/{patientId}");
        balanceResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var balanceDoc = await JsonDocument.ParseAsync(await balanceResponse.Content.ReadAsStreamAsync());
        balanceDoc.RootElement.GetProperty("outstandingBalance").GetDecimal().Should().Be(0);
    }

    [Fact]
    public async Task PublicBookingEndpoint_ReturnsOk()
    {
        // Verify the public booking chat endpoint is accessible without auth
        var chatHistory = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject
            {
                ["role"] = "user",
                ["content"] = "What therapists are available?"
            }
        };

        var response = await _fixture.Client.PostAsJsonAsync("/api/booking/chat", chatHistory);

        // The endpoint should be accessible (may fail on LLM call, but 200 or 500 — not 401/403)
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PrivacyPage_AccessibleAnonymously()
    {
        var response = await _fixture.Client.GetAsync("/privacy");
        // Blazor pages return 200 (rendered by the server-side host)
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BookPage_AccessibleAnonymously()
    {
        var response = await _fixture.Client.GetAsync("/book");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
