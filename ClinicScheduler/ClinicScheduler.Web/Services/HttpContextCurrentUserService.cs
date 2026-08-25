using System.Security.Claims;
using ClinicScheduler.Core.Auth;
using ClinicScheduler.Core.Interfaces;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Resolves the current user from the ambient HTTP context for audit attribution.
/// Covers API requests and the initial Blazor circuit request; returns null for
/// background services. Registered as a singleton — IHttpContextAccessor is
/// async-local, so each request sees its own user.
/// </summary>
public sealed class HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    /// <inheritdoc/>
    public string? UserId =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <inheritdoc/>
    public string? UserName =>
        httpContextAccessor.HttpContext?.User.Identity?.Name;

    /// <inheritdoc/>
    public int? TenantId =>
        int.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue(ClinicClaimTypes.ClinicId), out var id)
            ? id
            : null;

    /// <inheritdoc/>
    public ClaimsPrincipal? Principal =>
        httpContextAccessor.HttpContext?.User;
}
