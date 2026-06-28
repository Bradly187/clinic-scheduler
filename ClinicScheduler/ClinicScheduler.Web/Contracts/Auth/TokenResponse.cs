namespace ClinicScheduler.Web.Contracts.Auth;

/// <summary>JWT returned by POST /api/auth/token on success.</summary>
public sealed class TokenResponse
{
    /// <summary>Signed JWT — include as: Authorization: Bearer {Token}</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>UTC expiry time of this token.</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Roles granted to the authenticated user.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}
