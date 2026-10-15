using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Microservices.Integration.Shared;

public static class IntegrationPolicies
{
    /// <summary>
    /// Configures modern .NET 10 Polly v8 standard resilience pipeline:
    /// Rate limiter -> Total timeout -> Retry with exponential backoff &amp; jitter -> Circuit breaker -> Attempt timeout.
    /// Zero-allocation, thread-safe, and natively integrated with OpenTelemetry.
    /// </summary>
    public static IHttpClientBuilder AddIntegrationResilience(
        this IHttpClientBuilder builder,
        int retryCount = 3,
        double breakDurationSeconds = 30)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = retryCount;
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(breakDurationSeconds);
        });

        return builder;
    }
}
