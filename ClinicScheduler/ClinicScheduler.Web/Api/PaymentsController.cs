using System.ComponentModel.DataAnnotations;
using ClinicScheduler.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ClinicScheduler.Web.Services;

namespace ClinicScheduler.Web.Api;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = RoleNames.StaffOrAbove)]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentGateway _gateway;
    private readonly StripeOptions _stripeOptions;

    public PaymentsController(IPaymentGateway gateway, IOptions<StripeOptions> stripeOptions)
    {
        _gateway = gateway;
        _stripeOptions = stripeOptions.Value;
    }

    /// <summary>Returns the Stripe publishable key for frontend use. Returns 404 if Stripe is not configured.</summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public ActionResult<object> GetConfig()
    {
        if (!_gateway.IsConfigured)
            return NotFound(new ProblemDetails { Detail = "Payment processing is not configured." });

        return Ok(new { publishableKey = _stripeOptions.PublishableKey });
    }

    /// <summary>Creates a PaymentIntent for the given amount. Returns the client secret for Stripe.js confirmation.</summary>
    [HttpPost("create-intent")]
    public async Task<ActionResult<object>> CreatePaymentIntent(CreatePaymentIntentRequest request, CancellationToken ct)
    {
        if (!_gateway.IsConfigured)
            return BadRequest(new ProblemDetails { Detail = "Payment processing is not configured." });

        var amountCents = (long)(request.Amount * 100);
        var result = await _gateway.CreatePaymentIntentAsync(
            amountCents, request.Currency ?? "usd", request.CustomerStripeId, request.Description, ct);

        if (result is null)
            return StatusCode(503, new ProblemDetails { Detail = "Payment gateway unavailable." });

        return Ok(new
        {
            paymentIntentId = result.PaymentIntentId,
            clientSecret = result.ClientSecret,
            status = result.Status
        });
    }

    /// <summary>Creates a Stripe Customer for a patient.</summary>
    [HttpPost("customers")]
    public async Task<ActionResult<object>> CreateCustomer(CreateCustomerRequest request, CancellationToken ct)
    {
        if (!_gateway.IsConfigured)
            return BadRequest(new ProblemDetails { Detail = "Payment processing is not configured." });

        var customerId = await _gateway.CreateCustomerAsync(request.Email, request.Name, ct);
        if (customerId is null)
            return StatusCode(503, new ProblemDetails { Detail = "Payment gateway unavailable." });

        return Ok(new { stripeCustomerId = customerId });
    }

    /// <summary>Refunds a PaymentIntent.</summary>
    [HttpPost("refund")]
    [Authorize(Roles = RoleNames.AdminOrManager)]
    public async Task<ActionResult<object>> RefundPayment(RefundRequest request, CancellationToken ct)
    {
        if (!_gateway.IsConfigured)
            return BadRequest(new ProblemDetails { Detail = "Payment processing is not configured." });

        var amountCents = request.Amount.HasValue ? (long?)(request.Amount.Value * 100) : null;
        var refundId = await _gateway.RefundPaymentAsync(request.PaymentIntentId, amountCents, ct);

        if (refundId is null)
            return StatusCode(503, new ProblemDetails { Detail = "Refund failed." });

        return Ok(new { refundId });
    }
}

public sealed class CreatePaymentIntentRequest
{
    [Required]
    [Range(0.01, 1000000)]
    public decimal Amount { get; init; }

    [StringLength(3)]
    public string? Currency { get; init; }

    [StringLength(500)]
    public string? CustomerStripeId { get; init; }

    [StringLength(500)]
    public string? Description { get; init; }
}

public sealed class CreateCustomerRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; init; } = string.Empty;
}

public sealed class RefundRequest
{
    [Required]
    public string PaymentIntentId { get; init; } = string.Empty;

    /// <summary>Amount to refund in dollars. Omit for full refund.</summary>
    [Range(0.01, 1000000)]
    public decimal? Amount { get; init; }
}
