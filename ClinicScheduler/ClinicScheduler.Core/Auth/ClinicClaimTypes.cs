namespace ClinicScheduler.Core.Auth;

/// <summary>
/// Custom claim types used to carry tenant context on the authenticated principal.
/// The tenant (clinic) is resolved from these claims server-side and never from a
/// caller-supplied argument. See <c>docs/multi-tenancy-design.md</c>.
/// </summary>
public static class ClinicClaimTypes
{
    /// <summary>
    /// Claim carrying the acting user's <c>ClinicId</c> (the tenant). Emitted on both the
    /// Identity sign-in cookie and the API JWT, and read back by <c>ICurrentUserService.TenantId</c>.
    /// </summary>
    public const string ClinicId = "clinic";
}
