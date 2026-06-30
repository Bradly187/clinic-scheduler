using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClinicScheduler.Core.Auth;
using ClinicScheduler.Infrastructure.Data;
using ClinicScheduler.Web.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace ClinicScheduler.Web.Api;

/// <summary>Issues JWT tokens for external REST API clients.</summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public class TokenController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _config;

    public TokenController(UserManager<AppUser> userManager, IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
    }

    /// <summary>
    /// Authenticates with email + password and returns a signed JWT.
    /// Include the token on subsequent requests as: <c>Authorization: Bearer {token}</c>
    /// </summary>
    [HttpPost("token")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<TokenResponse>> Token(
        [FromBody] TokenRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { error = "Invalid credentials." });

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(ClaimTypes.Name,               user.Email!),
        };
        // Tenant claim: scopes every request made with this token to the user's clinic.
        if (user.ClinicId is { } clinicId)
            claims.Add(new Claim(ClinicClaimTypes.ClinicId, clinicId.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var issuer   = _config["Jwt:Issuer"]   ?? "ClinicAgent";
        var audience = _config["Jwt:Audience"] ?? "ClinicAgent";
        var rawKey   = _config["Jwt:SigningKey"]
            ?? "dev-only-signing-key-not-for-production-use-changeme";
        var expiryHours = _config.GetValue<int>("Jwt:ExpiryHours", 8);

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(rawKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddHours(expiryHours);

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            expiry,
            signingCredentials: creds);

        return Ok(new TokenResponse
        {
            Token     = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiry,
            Roles     = roles.ToList(),
        });
    }
}
