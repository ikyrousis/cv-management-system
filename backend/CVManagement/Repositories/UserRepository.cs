using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.DTOs;
using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Repositories;

internal sealed class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context) => _context = context;

    public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => _context.Users.FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

    public Task<User?> FindAsync(int id, CancellationToken cancellationToken = default)
        => _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<UserDto>> GetAllAsync(int? page = null, int? pageSize = null, CancellationToken cancellationToken = default)
        => await Paging.Apply(_context.Users.OrderBy(u => u.Id), page, pageSize)
            .Select(u => new UserDto(u.Id, u.Username, u.Role, u.IsActive))
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default)
        => _context.Users.CountAsync(cancellationToken);

    public Task<int> CountActiveInRoleAsync(string role, CancellationToken cancellationToken = default)
        => _context.Users.CountAsync(u => u.IsActive && u.Role == role, cancellationToken);

    public void Add(User user) => _context.Users.Add(user);

    public async Task<bool> AddWhenEmptyAsync(User user, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // UPDLOCK with HOLDLOCK serializes concurrent bootstraps on the Users table: the second
        // transaction blocks until the first commits, then sees a non-empty table and refuses.
        var existing = await _context.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM Users WITH (UPDLOCK, HOLDLOCK)")
            .SingleAsync(cancellationToken);

        if (existing != 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }
}
