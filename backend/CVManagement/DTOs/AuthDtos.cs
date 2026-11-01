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

/// The access half of a session: a short-lived bearer token and its expiry. The refresh half is
/// issued alongside it and travels only in the combined TokenResponse.
public sealed record AccessTokenResponse(string Token, DateTime ExpiresAtUtc);

/// The full session the auth endpoints hand out: a short-lived access token plus the single-use
/// refresh credential that replaces it. The refresh token is shown once here and never stored.
public sealed record TokenResponse(
    string Token,
    DateTime ExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshExpiresAtUtc);

/// The refresh endpoint's contract: the opaque credential issued with the previous session.
/// It is sent exactly as received, because it is an opaque credential, not text to normalize.
public sealed class RefreshRequest
{
    [Required(ErrorMessage = "Refresh token is required")]
    public string RefreshToken { get; set; } = string.Empty;
}

/// The projection every user surface answers with; the password hash never leaves persistence.
public sealed record UserDto(int Id, string Username, string Role, bool IsActive);
