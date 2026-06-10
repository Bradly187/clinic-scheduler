using ClinicScheduler.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ClinicScheduler.Web.Api;

/// <summary>Handles cookie-based authentication for the Blazor server app.</summary>
[Route("account")]
[AllowAnonymous]
public class AccountController(SignInManager<AppUser> signInManager) : Controller
{
    /// <summary>Signs in a user with email and password, then redirects to <paramref name="returnUrl"/>.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl)
    {
        var result = await signInManager.PasswordSignInAsync(
            email, password, isPersistent: false, lockoutOnFailure: true);

        if (result.Succeeded)
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : Redirect("/");

        if (result.RequiresTwoFactor)
            return Redirect($"/login/2fa?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

        if (result.IsLockedOut)
            return Redirect($"/login?error=2&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

        return Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }

    /// <summary>
    /// Completes a two-factor sign-in with an authenticator code (6 digits) or a
    /// recovery code. Requires the pending two-factor cookie set by the first
    /// login step.
    /// </summary>
    [HttpPost("login-2fa")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> LoginTwoFactor(
        [FromForm] string code,
        [FromForm] string? returnUrl,
        [FromForm] bool rememberMachine = false)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            // The two-factor session expired — start over
            return Redirect($"/login?error=3&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
        }

        var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty);

        // 6-digit numeric codes come from the authenticator app; anything else
        // is treated as a recovery code
        var result = normalized.Length == 6 && normalized.All(char.IsDigit)
            ? await signInManager.TwoFactorAuthenticatorSignInAsync(
                normalized, isPersistent: false, rememberClient: rememberMachine)
            : await signInManager.TwoFactorRecoveryCodeSignInAsync(normalized);

        if (result.Succeeded)
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : Redirect("/");

        if (result.IsLockedOut)
            return Redirect($"/login?error=2&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

        return Redirect($"/login/2fa?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }

    /// <summary>Signs the current user out and redirects to the login page.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return Redirect("/login");
    }
}
