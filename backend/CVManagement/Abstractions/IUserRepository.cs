using CVManagement.DTOs;
using CVManagement.Entities;

namespace CVManagement.Abstractions;

public interface IUserRepository
{
    /// Tracked user for the login path; decrypts the stored hash for verification.
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> FindAsync(int id, CancellationToken cancellationToken = default);

    /// Projects into the read model; the password hash never materializes.
    Task<IReadOnlyList<UserDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<int> CountActiveInRoleAsync(string role, CancellationToken cancellationToken = default);

    void Add(User user);

    /// The bootstrap guard: adds the user only if no user exists, under a transaction that holds a
    /// lock over the count, so two simultaneous bootstraps cannot both succeed.
    Task<bool> AddWhenEmptyAsync(User user, CancellationToken cancellationToken = default);

    /// Atomically replaces the account's stored hash and revokes every refresh credential it
    /// holds: both effects commit together or neither does.
    Task ResetPasswordAsync(int userId, string newPasswordHash, DateTime revokedAtUtc, CancellationToken cancellationToken = default);
}
