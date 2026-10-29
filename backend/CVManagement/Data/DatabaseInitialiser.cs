using Microsoft.Data.SqlClient;

namespace CVManagement.Data;

public interface IDatabaseInitialiser
{
    Task InitialiseAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates the database on first run, retrying while SQL Server finishes starting.</summary>
internal sealed class DatabaseInitialiser : IDatabaseInitialiser
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly ApplicationDbContext _context;
    private readonly ILogger<DatabaseInitialiser> _logger;

    public DatabaseInitialiser(ApplicationDbContext context, ILogger<DatabaseInitialiser> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _context.Database.EnsureCreatedAsync(cancellationToken);
                _logger.LogInformation("Database ready after {Attempts} attempt(s)", attempt);
                return;
            }
            catch (SqlException ex) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(ex, "Database not reachable (attempt {Attempt}/{MaxAttempts}), retrying", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }
}
