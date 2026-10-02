using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace BMSL_Tracker.Services;

public sealed record ExternalProvisioningResult(IdentityResult Result, IdentityUser? User);

public interface IExternalUserProvisioner
{
    /// <summary>
    /// Creates the local Identity account that corresponds to a first-time external (Google) login:
    /// derives a unique username, trusts the provider-verified e-mail and links the external key.
    /// Rolls the create back if the link fails so a half-provisioned user is never left behind.
    /// </summary>
    Task<ExternalProvisioningResult> ProvisionAsync(ExternalLoginInfo info, CancellationToken cancellationToken = default);

    /// <summary>Display-name claim to include when signing the provisioned user in.</summary>
    IEnumerable<Claim> BuildDisplayNameClaims(ExternalLoginInfo info);
}

public sealed class ExternalUserProvisioner : IExternalUserProvisioner
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<ExternalUserProvisioner> _logger;

    public ExternalUserProvisioner(UserManager<IdentityUser> userManager, ILogger<ExternalUserProvisioner> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<ExternalProvisioningResult> ProvisionAsync(
        ExternalLoginInfo info, CancellationToken cancellationToken = default)
    {
        var email = ExternalUserNames.GetEmail(info.Principal);
        if (string.IsNullOrWhiteSpace(email))
        {
            return new ExternalProvisioningResult(
                IdentityResult.Failed(new IdentityError
                {
                    Code = "MissingExternalEmail",
                    Description = "The external provider did not return an e-mail address.",
                }),
                User: null);
        }

        var displayName = ExternalUserNames.GetDisplayName(info.Principal);
        var baseName = ExternalUserNames.BuildBaseUserName(email, displayName);

        var userName = await ExternalUserNames.ReserveUniqueUserNameAsync(
            baseName,
            async candidate => await _userManager.FindByNameAsync(candidate) is not null,
            cancellationToken);

        var user = new IdentityUser
        {
            UserName = userName,
            Email = email,
            // Google only returns verified addresses back to the relying party.
            EmailConfirmed = true,
        };

        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            return new ExternalProvisioningResult(created, User: null);
        }

        var linked = await _userManager.AddLoginAsync(user, info);
        if (!linked.Succeeded)
        {
            _logger.LogWarning(
                "External login link failed for user {UserName}; rolling back created account.",
                user.UserName);
            await _userManager.DeleteAsync(user);
            return new ExternalProvisioningResult(linked, User: null);
        }

        _logger.LogInformation(
            "Provisioned account {UserName} from {Provider} login.", user.UserName, info.LoginProvider);

        return new ExternalProvisioningResult(IdentityResult.Success, user);
    }

    public IEnumerable<Claim> BuildDisplayNameClaims(ExternalLoginInfo info)
    {
        var displayName = ExternalUserNames.GetDisplayName(info.Principal);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return [];
        }

        return [new Claim(ExternalUserNames.DisplayNameClaimType, displayName)];
    }
}
