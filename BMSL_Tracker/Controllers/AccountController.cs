using BMSL_Tracker.Models;
using BMSL_Tracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BMSL_Tracker.Controllers;

/// <summary>
/// Local credential login/registration plus Google external sign-in ("sign in" and, on first use,
/// automatic "sign up"). Authentication endpoints are rate limited per client IP (policy "auth").
/// </summary>
[EnableRateLimiting("auth")]
public class AccountController : Controller
{
    /// <summary>
    /// RFC 2606 reserved TLD — placeholder e-mails for local accounts can never collide with a
    /// real address while <c>User.RequireUniqueEmail</c> is enforced.
    /// </summary>
    internal const string NoEmailDomain = "@no-email.invalid";

    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IExternalUserProvisioner _externalUserProvisioner;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        IExternalUserProvisioner externalUserProvisioner,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _externalUserProvisioner = externalUserProvisioner;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // Registration
    // ------------------------------------------------------------------

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register(string? returnUrl = null)
    {
        return View(new RegisterViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = new IdentityUser
        {
            UserName = model.Username,
            Email = string.IsNullOrWhiteSpace(model.Email) ? model.Username + NoEmailDomain : model.Email.Trim(),
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            _logger.LogInformation("Created local account {UserName}.", user.UserName);
            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToLocal(model.ReturnUrl);
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return View(model);
    }

    // ------------------------------------------------------------------
    // Password login
    // ------------------------------------------------------------------

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Accept either the user name or the e-mail address as the identifier.
        var identifier = model.Username.Trim();
        var user = await _userManager.FindByNameAsync(identifier)
                   ?? await _userManager.FindByEmailAsync(identifier);

        if (user is not null)
        {
            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                return RedirectToLocal(model.ReturnUrl);
            }

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty,
                    "This account is temporarily locked out after several failed sign-in attempts. Try again later.");
                return View(model);
            }
        }

        // Deliberately identical message for unknown user vs wrong password (no user enumeration).
        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    // ------------------------------------------------------------------
    // External (Google) sign-in / sign-up
    // ------------------------------------------------------------------

    [HttpGet]
    [ActionName("ExternalLogin")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginGet(string? provider = null, string? returnUrl = null)
        => await ChallengeExternalAsync(provider, returnUrl);

    [HttpPost]
    [ActionName("ExternalLogin")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginPost(string? provider = null, string? returnUrl = null)
        => await ChallengeExternalAsync(provider, returnUrl);

    private async Task<IActionResult> ChallengeExternalAsync(string? provider, string? returnUrl)
    {
        // Only known external schemes may be challenged (prevents probing/arbitrary redirects).
        var allowed = (await _signInManager.GetExternalAuthenticationSchemesAsync())
            .Select(s => s.Name)
            .ToArray();

        if (string.IsNullOrEmpty(provider) || !allowed.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        var redirectUrl = Url.Action(
            nameof(ExternalLoginCallback),
            "Account",
            new { returnUrl = SafeReturnUrl(returnUrl) },
            Request.Scheme);

        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (!string.IsNullOrEmpty(remoteError))
        {
            _logger.LogWarning("External login failed remotely: {RemoteError}", remoteError);
            TempData["Message"] = "The external provider reported an error. Please try again.";
            TempData["MessageIsError"] = true;
            return RedirectToAction(nameof(Login));
        }

        ExternalLoginInfo? info;
        try
        {
            info = await _signInManager.GetExternalLoginInfoAsync();
        }
        catch (InvalidOperationException)
        {
            // Corrupted or mismatched XSRF token in the external cookie.
            info = null;
        }

        if (info is null)
        {
            TempData["Message"] = "Could not read the external login information. Please try again.";
            TempData["MessageIsError"] = true;
            return RedirectToAction(nameof(Login));
        }

        // Case: a signed-in local user is attaching a Google account to it.
        if (User.Identity?.IsAuthenticated == true)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser is not null)
            {
                var existingOwner = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                if (existingOwner is not null && existingOwner.Id != currentUser.Id)
                {
                    TempData["Message"] = "That Google account is already linked to a different user.";
                    TempData["MessageIsError"] = true;
                    return RedirectToLocal(returnUrl);
                }

                if (existingOwner is null)
                {
                    var linkResult = await _userManager.AddLoginAsync(currentUser, info);
                    if (linkResult.Succeeded)
                    {
                        TempData["Message"] = "Google account linked successfully.";
                    }
                    else
                    {
                        TempData["Message"] = string.Join(" ", linkResult.Errors.Select(e => e.Description));
                        TempData["MessageIsError"] = true;
                    }
                }

                return RedirectToLocal(returnUrl);
            }
        }

        // Case: sign in an existing user via their linked external login.
        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            return RedirectToLocal(returnUrl);
        }

        if (signInResult.IsLockedOut || signInResult.IsNotAllowed)
        {
            TempData["Message"] = "This account cannot sign in right now. Please contact an administrator.";
            TempData["MessageIsError"] = true;
            return RedirectToAction(nameof(Login));
        }

        // Case: first-time external login → create the account (external sign-up).
        var provisioning = await _externalUserProvisioner.ProvisionAsync(info, HttpContext.RequestAborted);
        if (!provisioning.Result.Succeeded || provisioning.User is null)
        {
            TempData["Message"] = provisioning.Result.Errors.Any()
                ? string.Join(" ", provisioning.Result.Errors.Select(e =>
                    string.IsNullOrWhiteSpace(e.Description) ? "Sign-up failed." : e.Description))
                : "Sign-up failed. Please try again or use a local account.";
            TempData["MessageIsError"] = true;
            return RedirectToAction(nameof(Login));
        }

        var claims = _externalUserProvisioner.BuildDisplayNameClaims(info);
        await _signInManager.SignInWithClaimsAsync(provisioning.User, isPersistent: false, claims);

        return RedirectToLocal(returnUrl);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string? SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private IActionResult RedirectToLocal(string? returnUrl)
        => returnUrl is not null && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(HomeController.Index), "Home");
}
