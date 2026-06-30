using System.Security.Claims;
using ClinicScheduler.Core.Auth;
using ClinicScheduler.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ClinicScheduler.Web.Services;

/// <summary>
/// Adds the tenant (<see cref="ClinicClaimTypes.ClinicId"/>) claim to the Identity sign-in
/// cookie principal, so cookie-authenticated requests carry the same tenant context the JWT
/// path emits in <c>TokenController</c>. The tenant comes from <see cref="AppUser.ClinicId"/> —
/// the server-side source of truth — never from a caller. See <c>docs/multi-tenancy-design.md</c>.
/// </summary>
public sealed class ClinicClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(userManager, roleManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.ClinicId is { } clinicId)
        {
            identity.AddClaim(new Claim(ClinicClaimTypes.ClinicId, clinicId.ToString()));
        }
        return identity;
    }
}
