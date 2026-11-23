using System.Security.Claims;
using ClinicSchedulerV2.Data;
using ClinicSchedulerV2.Data.Model;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicSchedulerV2.Web.Identity;

public static class Roles
{
    public const string Staff = "Staff";
    public const string Manager = "Manager";
    public const string Provider = "Provider";
    public const string Patient = "Patient";
    public const string StaffOrProvider = "Staff,Provider";
}

public static class ClinicClaims
{
    public const string EmployeeId = "employee_id";
    public const string PatientId = "patient_id";
    public const string DisplayName = "display_name";
}

/// <summary>
/// Roles are not stored: they follow from who the account belongs to. An employee in
/// <c>staff</c> is Staff (and Manager when their job title is Clinic Manager), an employee in
/// <c>provider</c> is a Provider, and an account linked to a patient is a Patient.
/// </summary>
public sealed class ClinicClaimsFactory(
    UserManager<AppUser> users, IOptions<IdentityOptions> options, IDbContextFactory<ClinicDbContext> contexts)
    : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        await using var db = await contexts.CreateDbContextAsync();

        if (user.EmployeeId is { } employeeId)
        {
            identity.AddClaim(new Claim(ClinicClaims.EmployeeId, employeeId.ToString()));
            var employee = await db.Employees.Include(e => e.Staff).Include(e => e.Provider)
                .SingleAsync(e => e.EmployeeId == employeeId);
            identity.AddClaim(new Claim(ClinicClaims.DisplayName,
                employee.Provider is { } p ? $"{employee.FullName}, {p.Credential}" : employee.FullName));
            if (employee.Staff is { } staff)
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Staff));
                if (staff.JobTitle == "Clinic Manager") identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Manager));
            }
            if (employee.Provider is not null) identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Provider));
        }
        else if (user.PatientId is { } patientId)
        {
            identity.AddClaim(new Claim(ClinicClaims.PatientId, patientId.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.Role, Roles.Patient));
            var patient = await db.Patients.SingleAsync(p => p.PatientId == patientId);
            identity.AddClaim(new Claim(ClinicClaims.DisplayName, patient.FullName));
        }
        return identity;
    }
}

/// <summary>The signed-in user, as the actor the database is told about.</summary>
public sealed class SignedInActorSource(AuthenticationStateProvider auth) : IActorSource
{
    public async ValueTask<Actor> GetActorAsync()
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true) return Actor.Nobody;
        return new Actor(
            Int(user, ClaimTypes.NameIdentifier), Int(user, ClinicClaims.EmployeeId), Int(user, ClinicClaims.PatientId),
            user.FindFirst(ClinicClaims.DisplayName)?.Value ?? user.Identity.Name ?? "");
    }

    private static int? Int(ClaimsPrincipal user, string type) =>
        int.TryParse(user.FindFirst(type)?.Value, out var value) ? value : null;
}

/// <summary>
/// Re-checks an open session every few minutes, so deactivating someone (which the database
/// turns into an account lockout) or resetting their password ends sessions already open.
/// </summary>
public sealed class RevalidatingAuthStateProvider(ILoggerFactory logging, IServiceScopeFactory scopes)
    : RevalidatingServerAuthenticationStateProvider(logging)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState state, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.GetUserAsync(state.User);
        if (user is null || await users.IsLockedOutAsync(user)) return false;
        if (!users.SupportsUserSecurityStamp) return true;
        var principalStamp = state.User.FindFirstValue(new ClaimsIdentityOptions().SecurityStampClaimType);
        return principalStamp == await users.GetSecurityStampAsync(user);
    }
}

/// <summary>
/// Creates demo logins for the sample data on a development machine, so nothing with a
/// password is committed. Runs only when sample data is on, a demo password is configured,
/// and no account exists yet.
/// </summary>
public static class DemoAccounts
{
    public static readonly (string UserName, int? EmployeeId, int? PatientId, string Who)[] All =
    [
        ("areyes", 11, null, "Angela Reyes, clinic manager"),
        ("kbrooks", 10, null, "Kevin Brooks, scheduler"),
        ("mgonzalez", 1, null, "Dr. Maria Gonzalez, MD"),
        ("rkim", 5, null, "Rachel Kim, RN"),
        ("lcarter", null, 1, "Linda Carter, patient"),
        ("dramirez", null, 6, "David Ramirez, patient"),
    ];

    public static async Task SeedAsync(IServiceProvider services, string password, ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.Users.AnyAsync()) return;

        var db = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        foreach (var (userName, employeeId, patientId, _) in All)
        {
            var email = employeeId is { } e
                ? (await db.Employees.SingleAsync(x => x.EmployeeId == e)).Email
                : (await db.Patients.SingleAsync(x => x.PatientId == patientId)).Email;
            var result = await users.CreateAsync(
                new AppUser { UserName = userName, Email = email, EmployeeId = employeeId, PatientId = patientId, EmailConfirmed = true },
                password);
            if (!result.Succeeded)
            {
                logger.LogWarning("Demo account {User} not created: {Errors}", userName,
                    string.Join("; ", result.Errors.Select(x => x.Description)));
                return;
            }
        }
        logger.LogInformation("Created {Count} demo accounts", All.Length);
    }
}
