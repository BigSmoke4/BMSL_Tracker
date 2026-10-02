using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using BMSL_Tracker.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace BMSL_Tracker.Controllers;

[EnableRateLimiting("account")]
public class AccountController : Controller
{
    private const string GoogleProvider = "Google";
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly IAccountEmailSender _emailSender;
    private readonly ILogger<AccountController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly string[] _allowedEmailDomains;

    public AccountController(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        IAuthenticationSchemeProvider schemeProvider,
        IAccountEmailSender emailSender,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _schemeProvider = schemeProvider;
        _emailSender = emailSender;
        _environment = environment;
        _allowedEmailDomains = (configuration.GetSection("Registration:AllowedEmailDomains").Get<string[]>() ?? [])
            .Select(domain => domain.Trim().TrimStart('@').TrimEnd('.'))
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Register(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl(returnUrl));
        }

        await PrepareAuthViewAsync();
        return View(new RegisterViewModel { ReturnUrl = GetLocalReturnUrl(returnUrl) });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PrepareAuthViewAsync();
            return View(model);
        }

        if (!IsAllowedRegistrationEmail(model.Email))
        {
            ModelState.AddModelError(string.Empty,
                "New account sign-up is restricted to approved organization email domains. Contact your administrator if you need access.");
            await PrepareAuthViewAsync();
            return View(model);
        }

        if (!_emailSender.IsConfigured)
        {
            ModelState.AddModelError(string.Empty,
                "Password sign-up is unavailable until outgoing email is configured. You can continue with Google if it is enabled.");
            await PrepareAuthViewAsync();
            return View(model);
        }

        var user = new IdentityUser
        {
            UserName = model.Username.Trim(),
            Email = model.Email.Trim()
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            await PrepareAuthViewAsync();
            return View(model);
        }

        try
        {
            await SendConfirmationEmailAsync(user);
            return RedirectToAction(nameof(RegisterConfirmation));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Password sign-up email could not be sent; removing the unconfirmed account.");
            await _userManager.DeleteAsync(user);
            ModelState.AddModelError(string.Empty,
                "We couldn’t send the confirmation email, so no account was created. Please try again later or use Google sign-in.");
            await PrepareAuthViewAsync();
            return View(model);
        }
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult RegisterConfirmation() => View();

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResendConfirmation()
    {
        ViewData["EmailEnabled"] = _emailSender.IsConfigured;
        return View(new ResendConfirmationViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ResendConfirmation(ResendConfirmationViewModel model)
    {
        if (ModelState.IsValid && _emailSender.IsConfigured)
        {
            var user = await _userManager.FindByEmailAsync(model.Email.Trim());
            if (user is not null && !user.EmailConfirmed)
            {
                try
                {
                    await SendConfirmationEmailAsync(user);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Keep the response generic so this endpoint cannot be used to enumerate accounts.
                    _logger.LogError(exception, "Could not resend an account confirmation email.");
                }
            }
        }

        return RedirectToAction(nameof(ResendConfirmationSent));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResendConfirmationSent() => View();

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            ViewData["ConfirmationSucceeded"] = false;
            return View();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            ViewData["ConfirmationSucceeded"] = false;
            return View();
        }

        try
        {
            var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, decodedToken);
            ViewData["ConfirmationSucceeded"] = result.Succeeded || user.EmailConfirmed;
        }
        catch (FormatException)
        {
            ViewData["ConfirmationSucceeded"] = false;
        }

        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl(returnUrl));
        }

        await PrepareAuthViewAsync();
        return View(new LoginViewModel { ReturnUrl = GetLocalReturnUrl(returnUrl) });
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PrepareAuthViewAsync();
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Username.Trim(), model.Password, model.RememberMe, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return LocalRedirect(GetSafeReturnUrl(model.ReturnUrl));
        }

        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "This account is temporarily locked after too many unsuccessful attempts. Try again later."
            : result.IsNotAllowed
                ? "Please confirm your email address before signing in. Check your inbox for the confirmation link."
                : "The username or password is incorrect.");

        await PrepareAuthViewAsync();
        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLogin(string? provider, string? returnUrl = null)
    {
        if (!string.Equals(provider, GoogleProvider, StringComparison.Ordinal))
        {
            return BadRequest();
        }

        if (await _schemeProvider.GetSchemeAsync(GoogleProvider) is null)
        {
            TempData["AuthMessage"] = "Google sign-in is not configured for this deployment yet.";
            return RedirectToAction(nameof(Login), new { returnUrl = GetLocalReturnUrl(returnUrl) });
        }

        var callbackUrl = Url.Action(
            nameof(ExternalLoginCallback),
            "Account",
            new { returnUrl = GetLocalReturnUrl(returnUrl) ?? Url.Action("Index", "Home") });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(GoogleProvider, callbackUrl!);
        return Challenge(properties, GoogleProvider);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            _logger.LogWarning("Google sign-in returned an error.");
            return AuthenticationError("Google sign-in could not be completed. Please try again.", returnUrl);
        }

        var externalLogin = await _signInManager.GetExternalLoginInfoAsync();
        if (externalLogin is null || !string.Equals(externalLogin.LoginProvider, GoogleProvider, StringComparison.Ordinal))
        {
            return AuthenticationError("We could not verify the Google sign-in. Please try again.", returnUrl);
        }

        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            externalLogin.LoginProvider,
            externalLogin.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: false);

        if (signInResult.Succeeded)
        {
            return LocalRedirect(GetSafeReturnUrl(returnUrl));
        }

        if (signInResult.IsLockedOut)
        {
            return AuthenticationError("This account is temporarily locked. Try again later.", returnUrl);
        }

        var email = externalLogin.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
        {
            return AuthenticationError("Google did not provide a valid email address for this account.", returnUrl);
        }

        var emailIsVerified = externalLogin.Principal.Claims
            .Where(claim => claim.Type is "urn:google:email_verified" or "urn:google:email_verified_v2")
            .Any(claim => string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase));
        if (!emailIsVerified)
        {
            return AuthenticationError("Google could not confirm this email address. Use a verified Google account and try again.", returnUrl);
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            // Only link a Google identity to an already-confirmed local account. This avoids
            // turning a pending, potentially pre-registered email into an account takeover.
            if (!user.EmailConfirmed)
            {
                return AuthenticationError(
                    "An account already uses this email. Confirm that account’s email address first, then sign in with Google.",
                    returnUrl);
            }

            var linkResult = await _userManager.AddLoginAsync(user, externalLogin);
            if (!linkResult.Succeeded)
            {
                _logger.LogWarning("Could not link Google login to Identity user {UserId}.", user.Id);
                return AuthenticationError("This Google account could not be linked. Please sign in with your existing method.", returnUrl);
            }

            await _signInManager.SignInAsync(user, isPersistent: false, authenticationMethod: GoogleProvider);
            return LocalRedirect(GetSafeReturnUrl(returnUrl));
        }

        if (!IsAllowedRegistrationEmail(email))
        {
            return AuthenticationError(
                "Self-sign-up is restricted to approved organization email domains. Contact your administrator if you need access.",
                returnUrl);
        }

        var userName = await CreateUniqueGoogleUserNameAsync(externalLogin.Principal, email);
        user = new IdentityUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Could not create an Identity account for a verified Google login.");
            return AuthenticationError("We could not create your account. Please try again or contact support.", returnUrl);
        }

        var addLoginResult = await _userManager.AddLoginAsync(user, externalLogin);
        if (!addLoginResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            _logger.LogWarning("Could not attach Google login to newly created Identity user {UserId}.", user.Id);
            return AuthenticationError("We could not finish creating your account. Please try again.", returnUrl);
        }

        await _signInManager.SignInAsync(user, isPersistent: false, authenticationMethod: GoogleProvider);
        return LocalRedirect(GetSafeReturnUrl(returnUrl));
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private async Task PrepareAuthViewAsync()
    {
        var googleEnabled = await _schemeProvider.GetSchemeAsync(GoogleProvider) is not null;
        var registrationEnabled = _environment.IsDevelopment() || _allowedEmailDomains.Length > 0;
        ViewData["GoogleLoginEnabled"] = googleEnabled;
        ViewData["GoogleSignupEnabled"] = googleEnabled && registrationEnabled;
        ViewData["LocalSignupEnabled"] = _emailSender.IsConfigured && registrationEnabled;
        ViewData["RegistrationEnabled"] = registrationEnabled;
        ViewData["AuthMessage"] = TempData["AuthMessage"];
    }

    private bool IsAllowedRegistrationEmail(string email)
    {
        if (_allowedEmailDomains.Length == 0)
        {
            return _environment.IsDevelopment();
        }

        var atIndex = email.LastIndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
        {
            return false;
        }

        var domain = email[(atIndex + 1)..].TrimEnd('.');
        return _allowedEmailDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }

    private IActionResult AuthenticationError(string message, string? returnUrl)
    {
        TempData["AuthMessage"] = message;
        return RedirectToAction(nameof(Login), new { returnUrl = GetLocalReturnUrl(returnUrl) });
    }

    private async Task<string> CreateUniqueGoogleUserNameAsync(ClaimsPrincipal principal, string email)
    {
        var displayName = principal.FindFirstValue(ClaimTypes.Name);
        var source = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName;
        var baseName = new string(source
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')
            .Take(28)
            .ToArray())
            .Trim('_', '-', '.');

        if (baseName.Length < 3)
        {
            baseName = "google_user";
        }

        var candidate = baseName;
        var suffix = 0;
        while (await _userManager.FindByNameAsync(candidate) is not null)
        {
            suffix++;
            var suffixText = $"_{suffix}";
            candidate = $"{baseName[..Math.Min(baseName.Length, 32 - suffixText.Length)]}{suffixText}";
        }

        return candidate;
    }

    private async Task SendConfirmationEmailAsync(IdentityUser user)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var confirmationUrl = Url.Action(
            nameof(ConfirmEmail),
            "Account",
            new { userId = user.Id, code },
            protocol: Request.Scheme);

        if (string.IsNullOrWhiteSpace(confirmationUrl) || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("Could not generate the account confirmation details.");
        }

        await _emailSender.SendConfirmationEmailAsync(user.Email, confirmationUrl, HttpContext.RequestAborted);
    }

    private string? GetLocalReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : null;

    private string GetSafeReturnUrl(string? returnUrl) =>
        GetLocalReturnUrl(returnUrl) ?? Url.Action("Index", "Home")!;

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }
}
