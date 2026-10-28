
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CVManagement.Data;

/// <summary>Healthy while the app can reach SQL Server.</summary>
internal sealed class SqlHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _context;

    public SqlHealthCheck(ApplicationDbContext context) => _context = context;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("SQL Server reachable")
                : HealthCheckResult.Unhealthy("SQL Server did not answer a connectivity check");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server connectivity check failed", ex);
        }
    }
}
