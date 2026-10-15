using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Microservices.Integration.Shared;

public static class IntegrationPolicies
{
    /// <summary>
    /// Configures modern .NET 10 Polly v8 standard resilience pipeline from BaseIntegrationOptions:
    /// Rate limiter -> Total timeout -> Retry with exponential backoff &amp; jitter -> Circuit breaker -> Attempt timeout.
    /// Zero-allocation, thread-safe, and natively integrated with OpenTelemetry.
    /// </summary>
    public static IHttpClientBuilder AddIntegrationResilience(
        this IHttpClientBuilder builder,
        BaseIntegrationOptions? options = null)
    {
        var opts = options ?? new BaseIntegrationOptions();
        var timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 10);
        var totalTimeout = TimeSpan.FromSeconds(timeout.TotalSeconds * (opts.RetryCount + 1));

        builder.AddStandardResilienceHandler(handler =>
        {
            handler.Retry.MaxRetryAttempts = opts.RetryCount;
            handler.Retry.BackoffType = DelayBackoffType.Exponential;
            handler.Retry.UseJitter = true;
            handler.AttemptTimeout.Timeout = timeout;
            handler.TotalRequestTimeout.Timeout = totalTimeout;
            handler.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        });

        return builder;
    }

    public static IHttpClientBuilder AddIntegrationResilience(
        this IHttpClientBuilder builder,
        int retryCount,
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
