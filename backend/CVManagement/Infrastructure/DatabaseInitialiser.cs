using CVManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace CVManagement.Infrastructure;

internal interface IDatabaseInitialiser
{
    Task InitialiseAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates the database on first run, retrying while SQL Server finishes starting.</summary>
internal sealed class DatabaseInitialiser : IDatabaseInitialiser
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly ApplicationDbContext _context;

    public DatabaseInitialiser(ApplicationDbContext context) => _context = context;

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync(cancellationToken);
                return;
            }
            catch (SqlException) when (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
