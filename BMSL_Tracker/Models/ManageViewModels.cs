using System.ComponentModel.DataAnnotations;

namespace BMSL_Tracker.Models;

/// <summary>Read-only profile information for the self-service manage page.</summary>
public class ManageViewModel
{
    public string UserName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool HasPassword { get; set; }
    public IReadOnlyList<Microsoft.AspNetCore.Identity.UserLoginInfo> AssociatedLogins { get; set; }
        = [];
    public string? ExternalReturnUrl { get; set; }
}

/// <summary>
/// Change (or, for external-only accounts, set) the local password.
/// "Current password" is required only when the account already has one; that rule is
/// enforced in the controller so passwordless Google accounts are not blocked by it.
/// </summary>
public class ChangePasswordViewModel
{
    [DataType(DataType.Password)]
    [StringLength(100)]
    [Display(Name = "Current password")]
    public string? CurrentPassword { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 8,
        ErrorMessage = "The {0} must be at least {2} characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
