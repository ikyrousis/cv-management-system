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

    Task<PagedResult<UserDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public interface ITokenProvider
{
    TokenResponse IssueToken(User user);
}
