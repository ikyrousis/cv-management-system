namespace CVManagement.Entities;

/// A single-use refresh credential. The API stores only the SHA-256 hash of the raw token, so a
/// stolen database file cannot be replayed against the API. Every use rotates the credential, and
/// replaying a revoked one revokes every token the account holds.
public class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// Plaintext by design, like the username: it is the unique lookup key, and it is already a
    /// one-way digest of a 256-bit random value, so there is nothing to protect by encrypting it.
    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// Set at first use, at logout and at deactivation. A replay while the token is unexpired is
    /// theft-shaped, and containment revokes the account's whole chain.
    public DateTime? RevokedAtUtc { get; set; }
}
