using Microsoft.Extensions.Diagnostics.HealthChecks;
using Trade.Infrastructure.Persistence;

namespace Trade.Infrastructure.Health;

internal sealed class DatabaseHealthCheck(TradeDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
    }
}
