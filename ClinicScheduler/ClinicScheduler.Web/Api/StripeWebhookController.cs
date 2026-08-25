using ClinicScheduler.Core.Entities;
using ClinicScheduler.Core.Interfaces;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;

namespace ClinicScheduler.Web.Api;

/// <summary>
/// Handles Stripe webhook events. This endpoint is unauthenticated (Stripe can't send
/// bearer tokens) but validates the webhook signature using the configured signing secret.
/// </summary>
[ApiController]
[Route("api/webhooks/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IPaymentGateway _gateway;
    private readonly ClinicDbContext _db;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(IPaymentGateway gateway, ClinicDbContext db, ILogger<StripeWebhookController> logger)
    {
        _gateway = gateway;
        _db = db;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook(CancellationToken ct)
    {
        var payload = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync(ct);
        var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("Stripe webhook received without Stripe-Signature header");
            return BadRequest("Missing Stripe-Signature header.");
        }

        var eventType = _gateway.ValidateWebhookSignature(payload, signatureHeader);
        if (eventType is null)
        {
            _logger.LogWarning("Stripe webhook signature validation failed");
            return BadRequest("Invalid signature.");
        }

        _logger.LogInformation("Received Stripe webhook: {EventType}", eventType);

        switch (eventType)
        {
            case "payment_intent.succeeded":
                await HandlePaymentSucceededAsync(payload, ct);
                break;

            case "payment_intent.payment_failed":
                await HandlePaymentFailedAsync(payload, ct);
                break;

            default:
                _logger.LogDebug("Unhandled Stripe event type: {EventType}", eventType);
                break;
        }

        return Ok();
    }

    private async Task HandlePaymentSucceededAsync(string payload, CancellationToken ct)
    {
        var stripeEvent = EventUtility.ParseEvent(payload);
        if (stripeEvent.Data.Object is not PaymentIntent intent)
            return;

        var payment = await _db.Payments
            .Include(p => p.Invoice)
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id, ct);

        if (payment is null)
        {
            _logger.LogDebug("No matching payment found for PaymentIntent {IntentId}", intent.Id);
            return;
        }

        if (payment.Status == PaymentStatus.Pending)
        {
            payment.MarkCompleted();
            payment.Invoice.RecordPayment(payment.Amount);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Payment {PaymentId} marked completed via webhook for intent {IntentId}",
                payment.Id, intent.Id);
        }
    }

    private async Task HandlePaymentFailedAsync(string payload, CancellationToken ct)
    {
        var stripeEvent = EventUtility.ParseEvent(payload);
        if (stripeEvent.Data.Object is not PaymentIntent intent)
            return;

        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == intent.Id, ct);

        if (payment is null)
        {
            _logger.LogDebug("No matching payment found for failed PaymentIntent {IntentId}", intent.Id);
            return;
        }

        if (payment.Status == PaymentStatus.Pending)
        {
            payment.MarkFailed();
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Payment {PaymentId} marked failed via webhook for intent {IntentId}",
                payment.Id, intent.Id);
        }
    }
}
