using System.ComponentModel.DataAnnotations;
using CVManagement.Validation;

namespace CVManagement.DTOs;

/// Credentials for the login endpoint. The password is deliberately not trimmed: spaces can be
/// legitimate password characters, so trimming would silently change the credential.
public sealed class LoginRequest
{
    private string _username = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [StringLength(AuthLimits.UsernameMaxLength, MinimumLength = AuthLimits.UsernameMinLength,
        ErrorMessage = "Username must be between 3 and 50 characters")]
    [RegularExpression(Patterns.Username,
        ErrorMessage = "Username may contain letters, digits, dots, underscores and hyphens only")]
    public string Username
    {
        get => _username;
        set => _username = value?.Trim() ?? string.Empty;
    }

    [Required(ErrorMessage = "Password is required")]
    [StringLength(AuthLimits.PasswordMaxLength, MinimumLength = AuthLimits.PasswordMinLength,
        ErrorMessage = "Password must be at least 12 characters")]
    public string Password { get; set; } = string.Empty;
}

/// Same shape as login, kept separate so the bootstrap endpoint states its own contract.
public sealed class BootstrapRequest
{
    private string _username = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [StringLength(AuthLimits.UsernameMaxLength, MinimumLength = AuthLimits.UsernameMinLength,
        ErrorMessage = "Username must be between 3 and 50 characters")]
    [RegularExpression(Patterns.Username,
        ErrorMessage = "Username may contain letters, digits, dots, underscores and hyphens only")]
    public string Username
    {
        get => _username;
        set => _username = value?.Trim() ?? string.Empty;
    }

    [Required(ErrorMessage = "Password is required")]
    [StringLength(AuthLimits.PasswordMaxLength, MinimumLength = AuthLimits.PasswordMinLength,
        ErrorMessage = "Password must be at least 12 characters")]
    public string Password { get; set; } = string.Empty;
}

/// Account creation is an admin action, never self-registration, hence the role is chosen here.
public sealed class CreateUserRequest
{
    private string _username = string.Empty;

    [Required(ErrorMessage = "Username is required")]
    [StringLength(AuthLimits.UsernameMaxLength, MinimumLength = AuthLimits.UsernameMinLength,
        ErrorMessage = "Username must be between 3 and 50 characters")]
    [RegularExpression(Patterns.Username,
        ErrorMessage = "Username may contain letters, digits, dots, underscores and hyphens only")]
    public string Username
    {
        get => _username;
        set => _username = value?.Trim() ?? string.Empty;
    }

    [Required(ErrorMessage = "Password is required")]
    [StringLength(AuthLimits.PasswordMaxLength, MinimumLength = AuthLimits.PasswordMinLength,
        ErrorMessage = "Password must be at least 12 characters")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role is required")]
    public string Role { get; set; } = string.Empty;
}

public sealed record TokenResponse(string Token, DateTime ExpiresAtUtc);

/// The projection every user surface answers with; the password hash never leaves persistence.
public sealed record UserDto(int Id, string Username, string Role, bool IsActive);
