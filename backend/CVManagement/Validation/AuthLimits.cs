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

    /// Raw refresh-token entropy: 256 bits of CSPRNG output, base64url-encoded on the wire.
    public const int RefreshTokenByteLength = 32;

    /// Lifetime of the rotating refresh credential. Every use issues a full new window, so an
    /// active session never re-logins; the cap is how long an abandoned device keeps access.
    public const int DefaultRefreshTokenLifetimeDays = 7;

    /// Deliberate work factor for PBKDF2. Raise with hardware review; never lower it silently.
    public const int Pbkdf2Iterations = 210_000;
}
