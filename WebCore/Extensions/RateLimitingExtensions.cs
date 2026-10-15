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

            AddWindowPolicy(options, DefaultPolicy, rateLimitOptions.AnonymousPerMinute, queueLimit: 5);
            AddWindowPolicy(options, AuthenticatedPolicy, rateLimitOptions.AuthenticatedPerMinute, queueLimit: 10);
            AddWindowPolicy(options, StrictPolicy, rateLimitOptions.StrictPerMinute, queueLimit: 2);
        });

        return services;
    }

    private static void AddWindowPolicy(RateLimiterOptions options, string policyName, int permitLimit, int queueLimit)
    {
        options.AddFixedWindowLimiter(policyName, limiterOptions =>
        {
            limiterOptions.Window = TimeSpan.FromMinutes(1);
            limiterOptions.PermitLimit = permitLimit;
            limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            limiterOptions.QueueLimit = queueLimit;
        });
    }

    public static IApplicationBuilder UseMicroserviceRateLimiting(this IApplicationBuilder app)
    {
        app.UseRateLimiter();
        return app;
    }
}
