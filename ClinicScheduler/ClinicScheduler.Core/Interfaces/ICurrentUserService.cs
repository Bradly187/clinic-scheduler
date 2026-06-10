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
}
