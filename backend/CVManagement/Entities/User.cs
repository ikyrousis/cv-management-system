namespace CVManagement.Entities;

/// A back-office account. There is no self-registration: the first account arrives through the
/// bootstrap endpoint, the rest are created by an administrator.
public class User
{
    public int Id { get; set; }

    /// Stored in plaintext by design: it is the login identifier, must support a unique index and
    /// an equality lookup, and is not personal data. The password hash is what stays protected.
    public string Username { get; set; } = string.Empty;

    /// Salted PBKDF2 hash produced by PasswordHasher, encrypted with the column machinery so a
    /// stolen database dump cannot be attacked offline without the encryption key.
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
