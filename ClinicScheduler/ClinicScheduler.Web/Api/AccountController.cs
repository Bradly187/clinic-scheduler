using ClinicScheduler.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ClinicScheduler.Web.Api;

/// <summary>Handles cookie-based authentication for the Blazor server app.</summary>
[Route("account")]
[AllowAnonymous]
public class AccountController(SignInManager<AppUser> signInManager) : Controller
{
    /// <summary>Signs in a user with email and password, then redirects to <paramref name="returnUrl"/>.</summary>
    [HttpPost("login")]
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
            return Redirect($"/login-2fa?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

        if (result.IsLockedOut)
            return Redirect($"/login?error=2&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

        return Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }

    /// <summary>Completes sign-in using a TOTP authenticator code.</summary>
    [HttpPost("login-2fa")]
    public async Task<IActionResult> LoginWithTwoFactor(
        [FromForm] string code,
        [FromForm] string? returnUrl)
    {
        var cleanCode = code.Replace(" ", "").Replace("-", "");
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            cleanCode, isPersistent: false, rememberClient: false);

        if (result.Succeeded)
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : Redirect("/");

        if (result.IsLockedOut)
            return Redirect($"/login-2fa?error=2&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");

        return Redirect($"/login-2fa?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }

    /// <summary>Completes sign-in using a single-use recovery code.</summary>
    [HttpPost("login-recovery")]
    public async Task<IActionResult> LoginWithRecoveryCode(
        [FromForm] string recoveryCode,
        [FromForm] string? returnUrl)
    {
        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(
            recoveryCode.Replace(" ", ""));

        if (result.Succeeded)
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : Redirect("/");

        return Redirect($"/login-2fa?error=3&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
    }

    /// <summary>Signs the current user out and redirects to the login page.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return Redirect("/login");
    }
}
