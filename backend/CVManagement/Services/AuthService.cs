using CVManagement.Abstractions;
using CVManagement.DTOs;
using CVManagement.Entities;
using CVManagement.Results;
using CVManagement.Validation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CVManagement.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenProvider _tokens;
    private readonly IRefreshTokenGenerator _refreshGenerator;
    private readonly IPasswordHasher<User> _hasher;
    private readonly TimeProvider _clock;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IUnitOfWork unitOfWork,
        ITokenProvider tokens,
        IRefreshTokenGenerator refreshGenerator,
        IPasswordHasher<User> hasher,
        TimeProvider clock)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
        _tokens = tokens;
        _refreshGenerator = refreshGenerator;
        _hasher = hasher;
        _clock = clock;
    }

    public async Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByUsernameAsync(request.Username, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return LoginFailed();
        }

        var verification = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            return LoginFailed();
        }

        if (verification != PasswordVerificationResult.Success)
        {
            // SuccessRehashNeeded / SuccessUpdateSalt: re-derive so stored hashes follow current policy.
            user.PasswordHash = _hasher.HashPassword(user, request.Password);
        }

        return Result<TokenResponse>.Ok(await IssueSession(user));
    }

    public async Task<Result<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var hash = _refreshGenerator.Hash(request.RefreshToken);

        var stored = await _refreshTokens.FindByHashAsync(hash, cancellationToken);

        if (stored is null)
        {
            return RefreshFailed();
        }

        if (stored.RevokedAtUtc is not null)
        {
            // A used token being presented means it leaked: only an attacker can be holding it,
            // so containment takes the account's whole chain with it.
            await _refreshTokens.RevokeAllForUserAsync(stored.UserId, now, cancellationToken);
            return RefreshFailed();
        }

        if (stored.ExpiresAtUtc <= now)
        {
            // Expiry is abandonment, not theft: no containment, the account's other tokens survive.
            return RefreshFailed();
        }

        var user = await _users.FindAsync(stored.UserId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return RefreshFailed();
        }

        var material = _refreshGenerator.Generate();

        var replacement = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = material.TokenHash,
            CreatedAtUtc = material.CreatedAtUtc,
            ExpiresAtUtc = material.ExpiresAtUtc
        };

        if (!await _refreshTokens.TryReplaceAsync(hash, now, replacement, cancellationToken))
        {
            // Another presentation consumed this credential first: same shape as a replay, and
            // because consume and replacement committed together, the sweep below sees the
            // successor that the winner stored.
            await _refreshTokens.RevokeAllForUserAsync(stored.UserId, now, cancellationToken);
            return RefreshFailed();
        }

        var access = _tokens.IssueToken(user);

        return Result<TokenResponse>.Ok(new TokenResponse(
            access.Token, access.ExpiresAtUtc, material.Token, material.ExpiresAtUtc));
    }

    public async Task<Result> RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        // Oracle-free by design: an unknown or already-used token answers success, because the
        // caller is authenticated and must not learn which credentials exist.
        var hash = _refreshGenerator.Hash(refreshToken);
        await _refreshTokens.TryRevokeByHashAsync(hash, _clock.GetUtcNow().UtcDateTime, cancellationToken);

        return Result.Success();
    }

    public async Task<Result<TokenResponse>> BootstrapAsync(BootstrapRequest request, CancellationToken cancellationToken = default)
    {
        var user = new User
        {
            Username = request.Username,
            Role = AuthLimits.AdminRole,
            IsActive = true
        };

        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        if (!await _users.AddWhenEmptyAsync(user, cancellationToken))
        {
            return Result<TokenResponse>.Fail(ServiceFailure.Validation(
                "Bootstrap refused: a user already exists. Log in as an administrator to create further users."));
        }

        return Result<TokenResponse>.Ok(await IssueSession(user));
    }

    public async Task<PagedResult<UserDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
    {
        var items = await _users.GetAllAsync(page, pageSize, cancellationToken);
        var totalCount = await _users.CountAsync(cancellationToken);

        return new PagedResult<UserDto> { Items = items, TotalCount = totalCount };
    }

    public async Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Role is not (AuthLimits.AdminRole or AuthLimits.UserRole))
        {
            return Result<UserDto>.Fail(ServiceFailure.Validation(
                $"Role must be '{AuthLimits.AdminRole}' or '{AuthLimits.UserRole}'."));
        }

        if (await _users.GetByUsernameAsync(request.Username, cancellationToken) is not null)
        {
            return Result<UserDto>.Fail(ServiceFailure.Validation("Username is already in use."));
        }

        var user = new User
        {
            Username = request.Username,
            Role = request.Role,
            IsActive = true
        };

        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _users.Add(user);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // A second account with the same username can race past the check above; the unique
            // index is the real guarantee and this is its friendly translation.
            return Result<UserDto>.Fail(ServiceFailure.Validation("Username is already in use."));
        }

        return Result<UserDto>.Ok(new UserDto(user.Id, user.Username, user.Role, user.IsActive));
    }

    public async Task<Result> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindAsync(id, cancellationToken);

        if (user is null)
        {
            return Result.Fail(ServiceFailure.NotFound($"User {id} was not found."));
        }

        // Deactivating the last active administrator would lock everyone out of user management.
        if (user.Role == AuthLimits.AdminRole
            && await _users.CountActiveInRoleAsync(AuthLimits.AdminRole, cancellationToken) <= 1)
        {
            return Result.Fail(ServiceFailure.Validation("The last active administrator cannot be deactivated."));
        }

        user.IsActive = false;

        // Deactivation closes the refresh path immediately, not just future logins: every token
        // the account holds dies with the account's ability to use it.
        await _refreshTokens.RevokeAllForUserAsync(id, _clock.GetUtcNow().UtcDateTime, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// Mints the full session: the short-lived access token plus a fresh refresh credential whose
    /// digest is persisted before the pair is handed out, so the response is always backed by a
    /// stored, revocable credential.
    private async Task<TokenResponse> IssueSession(User user)
    {
        var access = _tokens.IssueToken(user);
        var material = _refreshGenerator.Generate();

        _refreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = material.TokenHash,
            CreatedAtUtc = material.CreatedAtUtc,
            ExpiresAtUtc = material.ExpiresAtUtc
        });

        await _unitOfWork.SaveChangesAsync();

        return new TokenResponse(access.Token, access.ExpiresAtUtc, material.Token, material.ExpiresAtUtc);
    }

    /// One shape for every login failure: the caller must not learn which part was wrong.
    private static Result<TokenResponse> LoginFailed()
        => Result<TokenResponse>.Fail(ServiceFailure.Unauthorized("Invalid username or password."));

    /// One shape for every refresh failure, including containment: the response reveals nothing
    /// about which credential state the presented token was in.
    private static Result<TokenResponse> RefreshFailed()
        => Result<TokenResponse>.Fail(ServiceFailure.Unauthorized("Invalid refresh token."));
}
