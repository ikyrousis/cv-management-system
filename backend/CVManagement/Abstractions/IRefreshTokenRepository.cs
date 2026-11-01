using CVManagement.Entities;

namespace CVManagement.Abstractions;

public interface IRefreshTokenRepository
{
    /// The stored record for a presented hash, or null. No expiry or revocation filtering:
    /// the service decides what each stored state means.
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// Atomic first-use consumption with replacement: the revoke and the successor's insert commit
    /// as one unit, and the consume is a single conditional UPDATE evaluated under the row lock,
    /// so two concurrent presentations cannot both succeed and a replay cannot race past the
    /// containment that follows it.
    Task<bool> TryReplaceAsync(string presentedTokenHash, DateTime revokedAtUtc, RefreshToken replacement, CancellationToken cancellationToken = default);

    /// Atomic single-token revocation, used by logout: one conditional UPDATE, so the outcome
    /// reports whether this call was the one that revoked it.
    Task<bool> TryRevokeByHashAsync(string tokenHash, DateTime revokedAtUtc, CancellationToken cancellationToken = default);

    /// Containment: kills every token the account holds, used when a revoked token is replayed.
    Task RevokeAllForUserAsync(int userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default);

    void Add(RefreshToken token);
}
