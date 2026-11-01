using CVManagement.Abstractions;
using CVManagement.Data;
using CVManagement.Entities;
using Microsoft.EntityFrameworkCore;

namespace CVManagement.Repositories;

internal sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly ApplicationDbContext _context;

    public RefreshTokenRepository(ApplicationDbContext context) => _context = context;

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);

    /// The consume is a single conditional UPDATE, so its revoked-state check is evaluated under
    /// the row lock at write time: whichever presenter runs first wins, every concurrent presenter
    /// matches zero rows. The successor's insert commits in the same transaction, so a later
    /// containment sweep always sees it and can never miss it.
    public async Task<bool> TryReplaceAsync(string presentedTokenHash, DateTime revokedAtUtc, RefreshToken replacement, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var consumed = await _context.RefreshTokens
            .Where(r => r.TokenHash == presentedTokenHash && r.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAtUtc, revokedAtUtc), cancellationToken) == 1;

        if (!consumed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        _context.RefreshTokens.Add(replacement);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// ExecuteUpdate is a single conditional UPDATE statement, so the revoked-state check and the
    /// write cannot interleave: two concurrent logout calls cannot both consume one token.
    public async Task<bool> TryRevokeByHashAsync(string tokenHash, DateTime revokedAtUtc, CancellationToken cancellationToken = default)
        => await _context.RefreshTokens
            .Where(r => r.TokenHash == tokenHash && r.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAtUtc, revokedAtUtc), cancellationToken) == 1;

    public async Task RevokeAllForUserAsync(int userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default)
        => await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevokedAtUtc, revokedAtUtc), cancellationToken);

    public void Add(RefreshToken token) => _context.RefreshTokens.Add(token);
}
