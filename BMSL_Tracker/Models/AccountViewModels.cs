using System.ComponentModel.DataAnnotations;

namespace BMSL_Tracker.Models;

public sealed class LoginViewModel
{
    [Required]
    [StringLength(256)]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Choose a username between 3 and 32 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9_.@+-]+$", ErrorMessage = "Use only letters, numbers, and . _ @ + - in your username.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Work email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "Use at least {2} characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class ResendConfirmationViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;
}
