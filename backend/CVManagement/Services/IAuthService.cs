using CVManagement.DTOs;
using CVManagement.Entities;
using CVManagement.Results;

namespace CVManagement.Services;

public interface IAuthService
{
    /// Fails with the same Unauthorized outcome for an unknown user, a wrong password and a
    /// deactivated account, so the response reveals nothing about which one it was.
    Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// Creates the first and only administrator when no user exists; refuses afterwards.
    Task<Result<TokenResponse>> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken = default);

    /// Exchanges a valid, unrevoked refresh credential for a new access and refresh pair. The
    /// presented token is revoked on first use; replaying a revoked one revokes the account's
    /// whole chain, because only an attacker can be holding a used token.
    Task<Result<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);

    /// Logout: revokes the presented refresh credential. Idempotent and oracle-free, because the
    /// caller is already authenticated and an unknown token leaves nothing to revoke.
    Task<Result> RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<PagedResult<UserDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public interface ITokenProvider
{
    AccessTokenResponse IssueToken(User user);
}

/// The freshly minted refresh credential: the raw token, its storage hash and its lifetime
/// window. The raw value crosses the wire once and never reaches persistence.
public sealed record RefreshTokenMaterial(string Token, string TokenHash, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);

public interface IRefreshTokenGenerator
{
    RefreshTokenMaterial Generate();

    /// The storage digest of a presented raw token, so a lookup uses exactly the value that
    /// was hashed when the credential was issued.
    string Hash(string rawToken);
}
