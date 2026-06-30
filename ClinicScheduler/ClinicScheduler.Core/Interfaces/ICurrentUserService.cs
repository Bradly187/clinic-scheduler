namespace ClinicScheduler.Core.Interfaces;

/// <summary>
/// Provides the identity of the user performing the current operation, for audit
/// attribution. Both values are null when no user context exists (background
/// services, migrations, seeding).
/// </summary>
public interface ICurrentUserService
{
    /// <summary>The ASP.NET Identity user ID of the current user, if any.</summary>
    string? UserId { get; }

    /// <summary>The user name (email) of the current user, if any.</summary>
    string? UserName { get; }

    /// <summary>
    /// The tenant (clinic) the current user belongs to, resolved from the principal's
    /// <see cref="Auth.ClinicClaimTypes.ClinicId"/> claim. Null when there is no user context
    /// (background services, migrations) or the user has no clinic. This is the server-side
    /// source of truth for tenant scoping — never a caller-supplied value.
    /// </summary>
    int? TenantId { get; }

    /// <summary>The ClaimsPrincipal of the current user, if any.</summary>
    System.Security.Claims.ClaimsPrincipal? Principal { get; }
}
