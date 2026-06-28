using System.ComponentModel.DataAnnotations;

namespace ClinicScheduler.Web.Contracts.Auth;

/// <summary>Credentials submitted to POST /api/auth/token to receive a JWT.</summary>
public sealed class TokenRequest
{
    /// <example>admin@clinic.com</example>
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    /// <example>Admin@1234</example>
    [Required]
    public string Password { get; init; } = string.Empty;
}
