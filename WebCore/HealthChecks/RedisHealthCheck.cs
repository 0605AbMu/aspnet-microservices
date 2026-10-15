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

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_multiplexer is null)
        {
            return HealthCheckResult.Unhealthy("Redis multiplexer is not registered.");
        }

        if (!_multiplexer.IsConnected)
        {
            return HealthCheckResult.Unhealthy("Redis is disconnected.");
        }

        try
        {
            var database = _multiplexer.GetDatabase();
            var latency = await database.PingAsync();

            var data = new Dictionary<string, object>
            {
                ["pingMs"] = Math.Round(latency.TotalMilliseconds, 2)
            };

            return HealthCheckResult.Healthy($"Redis is responsive (ping: {latency.TotalMilliseconds:F1}ms)", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis health check ping failed.", ex);
        }
    }
}
