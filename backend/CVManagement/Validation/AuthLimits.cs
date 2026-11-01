namespace CVManagement.Validation;

/// Single declaration of the account rules shared by every auth surface.
public static class AuthLimits
{
    public const int UsernameMinLength = 3;

    public const int UsernameMaxLength = 50;

    /// Length over composition, per current guidance: no forced character classes.
    public const int PasswordMinLength = 12;

    public const int PasswordMaxLength = 128;

    public const string AdminRole = "Admin";

    public const string UserRole = "User";

    public const int DefaultAccessTokenLifetimeMinutes = 60;

    /// Deliberate work factor for PBKDF2. Raise with hardware review; never lower it silently.
    public const int Pbkdf2Iterations = 210_000;
}
