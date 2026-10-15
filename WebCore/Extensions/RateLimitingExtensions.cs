using System.Threading.RateLimiting;
using Microservices.Common.Constants;
using Microservices.Common.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.WebCore.Extensions;

public static class RateLimitingExtensions
{
    public const string DefaultPolicy = "default";
    public const string AuthenticatedPolicy = "authenticated";
    public const string StrictPolicy = "strict";

    public static IServiceCollection AddMicroserviceRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var rateLimitOptions = configuration.GetSection(ConfigSections.RateLimiting).Get<RateLimitOptions>() 
            ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, token) =>
            {
                await context.HttpContext.WriteErrorResponseAsync(
                    StatusCodes.Status429TooManyRequests,
                    "too_many_requests",
                    cancellationToken: token);
            };

            // Global partitioned limiter: isolates each user/IP automatically
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var partitionKey = ResolvePartitionKey(httpContext);
                var isAuth = httpContext.User?.Identity?.IsAuthenticated == true;
                var permitLimit = isAuth ? rateLimitOptions.AuthenticatedPerMinute : rateLimitOptions.AnonymousPerMinute;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = isAuth ? 10 : 5
                    });
            });

            // Strict policy for sensitive operations
            options.AddPolicy(StrictPolicy, httpContext =>
            {
                var partitionKey = ResolvePartitionKey(httpContext);
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"strict:{partitionKey}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = rateLimitOptions.StrictPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 2
                    });
            });
        });

        return services;
    }

    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        var userId = httpContext.User?.FindFirst(AppClaimTypes.Sub)?.Value 
            ?? httpContext.User?.FindFirst(AppClaimTypes.UserId)?.Value;

        if (!string.IsNullOrEmpty(userId))
            return $"user:{userId}";

        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        return !string.IsNullOrEmpty(ip) ? $"ip:{ip}" : "anonymous";
    }

    public static IApplicationBuilder UseMicroserviceRateLimiting(this IApplicationBuilder app)
    {
        app.UseRateLimiter();
        return app;
    }
}
