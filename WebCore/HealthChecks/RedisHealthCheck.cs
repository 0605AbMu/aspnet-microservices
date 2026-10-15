using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Microservices.WebCore.HealthChecks;

/// <summary>
/// Health check that verifies connectivity to the configured Redis instance.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer? _multiplexer;

    public RedisHealthCheck(IServiceProvider serviceProvider)
    {
        _multiplexer = serviceProvider.GetService(typeof(IConnectionMultiplexer)) as IConnectionMultiplexer;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_multiplexer is not null && _multiplexer.IsConnected)
            {
                return Task.FromResult(HealthCheckResult.Healthy("Redis connection is active."));
            }

            return Task.FromResult(HealthCheckResult.Unhealthy("Redis is disconnected or unavailable."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Redis health check failed.", ex));
        }
    }
}
