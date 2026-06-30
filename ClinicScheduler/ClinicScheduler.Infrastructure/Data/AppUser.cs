using Microsoft.AspNetCore.Identity;

namespace ClinicScheduler.Infrastructure.Data;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The tenant (clinic) this user belongs to. This is the server-side source of truth from
    /// which every request's tenant is derived — it flows into the JWT as the <c>clinic</c> claim
    /// and on into the data layer's query filter. See <c>docs/multi-tenancy-design.md</c>.
    ///
    /// Nullable for now: platform-level/system users may have no clinic, and existing users are
    /// backfilled to the default clinic by migration. Made required in a later stage.
    /// </summary>
    public int? ClinicId { get; set; }
}
