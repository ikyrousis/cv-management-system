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
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITokenProvider _tokens;
    private readonly IPasswordHasher<User> _hasher;

    public AuthService(
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ITokenProvider tokens,
        IPasswordHasher<User> hasher)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _tokens = tokens;
        _hasher = hasher;
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
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<TokenResponse>.Ok(_tokens.IssueToken(user));
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

        return Result<TokenResponse>.Ok(_tokens.IssueToken(user));
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
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// One shape for every login failure: the caller must not learn which part was wrong.
    private static Result<TokenResponse> LoginFailed()
        => Result<TokenResponse>.Fail(ServiceFailure.Unauthorized("Invalid username or password."));
}
