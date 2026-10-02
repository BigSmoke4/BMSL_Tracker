using System.ComponentModel.DataAnnotations;

namespace BMSL_Tracker.Models;

public class LoginViewModel
{
    [Required]
    [Display(Name = "User name or e-mail")]
    [StringLength(256, MinimumLength = 2)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me?")]
    public bool RememberMe { get; set; }

    /// <summary>Local-only URL to return to after sign-in (validated server-side).</summary>
    [Display(Name = "Return URL")]
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required]
    [StringLength(64, MinimumLength = 4,
        ErrorMessage = "The {0} must be between {2} and {1} characters long.")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$",
        ErrorMessage = "The {0} may only contain letters, digits, '.', '_' and '-'.")]
    [Display(Name = "User name")]
    public string Username { get; set; } = string.Empty;

    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "E-mail (optional)")]
    public string? Email { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 8,
        ErrorMessage = "The {0} must be at least {2} characters long.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Local-only URL to return to after registration (validated server-side).</summary>
    public string? ReturnUrl { get; set; }
}
