using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CVManagement.Data;

/// Healthy while the app can reach SQL Server.
internal sealed class SqlHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SqlHealthCheck> _logger;

    public SqlHealthCheck(ApplicationDbContext context, ILogger<SqlHealthCheck> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("SQL Server reachable")
                : Unhealthy("SQL Server did not answer a connectivity check", null);
        }
        catch (Exception ex)
        {
            return Unhealthy("SQL Server connectivity check failed", ex);
        }
    }

    private HealthCheckResult Unhealthy(string description, Exception? exception)
    {
        _logger.LogWarning(exception, "Health check unhealthy: {Description}", description);
        return HealthCheckResult.Unhealthy(description, exception);
    }
}
