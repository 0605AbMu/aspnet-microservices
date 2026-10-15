using Microservices.Common.Cache;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microservices.Common.Lock;
using Microservices.Common.Persistence.Interceptors;
using Microservices.Common.Storage;
using Microservices.WebCore.Audit;
using Microservices.WebCore.Cache;
using Microservices.WebCore.Context;
using Microservices.WebCore.Interceptors;
using Microservices.WebCore.Lock;
using Microservices.WebCore.Logging;
using Microservices.WebCore.Middleware;
using Microservices.WebCore.OpenApi;
using Microservices.WebCore.Security;
using Microservices.WebCore.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Context;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerUI;
using Minio;

namespace Microservices.WebCore.Extensions;

public static class WebCoreExtensions
{
    /// <summary>
    /// Registers all shared infrastructure: Auth, Cache, gRPC interceptors,
    /// Rate limiting, Compression, OpenTelemetry, OpenAPI, Correlation ID,
    /// Idempotency, Session Validation, Distributed Lock, Audit Publisher, MinIO Storage, and Controllers.
    /// </summary>
    public static IServiceCollection AddWebCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        // --- Context (Scoped — per request) ---
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserHttpContext>();
        services.AddScoped<ITenantContext, TenantContextHttpContext>();

        // --- Shared Object Storage (MinIO) ---
        services.AddSingleton<Minio.IMinioClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var endpoint = config["Minio:Endpoint"] ?? "localhost:9000";
            var accessKey = config["Minio:AccessKey"] ?? "minioadmin";
            var secretKey = config["Minio:SecretKey"] ?? "minioadmin";
            var withSsl = config.GetValue<bool>("Minio:WithSSL");

            return new Minio.MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(withSsl)
                .Build();
        });
        services.AddScoped<IFileStorageService, MinioFileStorageService>();

        // --- Audit & Outbox Publisher ---
        services.AddScoped<IAuditOutboxPublisher, MassTransitAuditPublisher>();
        services.AddSingleton<AuditSaveChangesInterceptor>();

        // --- JWT / JWKS Authentication ---
        services.AddMicroserviceJwtAuth(configuration);

        // --- RBAC & Permissions ---
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // --- HybridCache (L1 In-Memory + L2 Redis) ---
        var redisConnectionString = configuration.GetConnectionString(ConnectionNames.Redis)
            ?? "localhost:6379";

        services.AddMemoryCache();
        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = redisConnectionString;
            opts.InstanceName = $"{serviceName}:";
        });

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

#pragma warning disable EXTEXP0018
        services.AddHybridCache(opts =>
        {
            opts.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                LocalCacheExpiration = TimeSpan.FromSeconds(30),
                Expiration = TimeSpan.FromMinutes(5)
            };
        });
#pragma warning restore EXTEXP0018

        services.AddScoped<ITenantCache, HybridTenantCache>();

        // --- gRPC Interceptors ---
        services.AddScoped<GrpcClientContextInterceptor>();
        services.AddSingleton<GrpcServerContextInterceptor>();
        services.AddSingleton<GrpcExceptionInterceptor>();

        // --- Rate Limiting ---
        services.AddMicroserviceRateLimiting(configuration);

        // --- Response Compression ---
        services.AddMicroserviceCompression();

        // --- OpenTelemetry ---
        services.AddMicroserviceOpenTelemetry(configuration);

        // --- Controllers & OpenAPI ---
        services.AddControllers();
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

        // --- Health Checks ---
        services.AddHealthChecks();

        // --- Graceful Shutdown (30s) ---
        services.Configure<HostOptions>(opts =>
        {
            opts.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        // --- Kestrel MaxRequestBodySize (5MB) ---
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 5 * 1024 * 1024;
        });

        // --- Serilog ---
        services.AddMicroserviceSerilog(configuration, serviceName);

        return services;
    }

    public static IServiceCollection AddMicroserviceSerilog(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        AppLogContext.CurrentServiceName = serviceName;

        const string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}] [{ClassName}.{MethodName}:{LineNumber}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.With(new CallerMethodEnricher())
            .WriteTo.Console(outputTemplate: outputTemplate)
            .CreateLogger();

        services.AddSerilog(Log.Logger);
        return services;
    }

    public static WebApplication MapMicroserviceHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true
        }).AllowAnonymous();

        app.MapHealthChecks("/health").AllowAnonymous();
        return app;
    }

    /// <summary>
    /// Applies all shared middleware pipeline in correct order.
    /// </summary>
    public static WebApplication UseWebCore(this WebApplication app)
    {
        // 1. Correlation ID middleware — must be first
        app.UseMiddleware<CorrelationIdMiddleware>();

        // 2. Ambient LogContext Middleware (Endpoint)
        app.Use(async (context, next) =>
        {
            using (LogContext.PushProperty("Endpoint", context.ResolveEndpointMethodName()))
            {
                await next();
            }
        });

        // 3. Serilog request logging
        app.UseSerilogRequestLogging(opts =>
        {
            opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("ServiceName", AppLogContext.CurrentServiceName);
                diagnosticContext.Set("ClassName", httpContext.ResolveEndpointClassName());
                diagnosticContext.Set("MethodName", httpContext.ResolveEndpointMethodName());
                diagnosticContext.Set("LineNumber", 0);
                diagnosticContext.Set(AppItemKeys.UserId,
                    httpContext.User.FindFirst(AppClaimTypes.Sub)?.Value ?? "anonymous");
                diagnosticContext.Set(AppItemKeys.CorrelationId,
                    httpContext.ResolveCorrelationId());
                diagnosticContext.Set(AppItemKeys.TenantId,
                    httpContext.User.FindFirst(AppClaimTypes.TenantId)?.Value ?? string.Empty);
            };
            opts.GetLevel = (httpContext, elapsed, ex) =>
                ex is not null ? Serilog.Events.LogEventLevel.Error
                : httpContext.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
                : httpContext.Response.StatusCode >= 400 ? Serilog.Events.LogEventLevel.Warning
                : Serilog.Events.LogEventLevel.Information;
        });

        // 4. Global exception handler
        app.UseMiddleware<GlobalExceptionMiddleware>();

        // 5. Response compression
        app.UseMicroserviceCompression();

        // 6. Rate limiting
        app.UseMicroserviceRateLimiting();

        // 7. Authentication
        app.UseAuthentication();

        // 8. Session / Token Revocation Check
        app.UseMiddleware<SessionValidationMiddleware>();

        // 9. Authorization
        app.UseAuthorization();

        // 10. HTTP Idempotency-Key deduplication
        app.UseMiddleware<IdempotencyMiddleware>();

        // 11. Health Checks
        app.MapMicroserviceHealthChecks();

        // 12. OpenAPI & Swagger UI
        app.MapOpenApi();

        var serviceTitle = AppLogContext.CurrentServiceName ?? app.Environment.ApplicationName;

        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "swagger";
            options.SwaggerEndpoint("/openapi/v1.json", $"{serviceTitle} v1");
            options.DocumentTitle = $"{serviceTitle} - Swagger UI";
            options.DisplayRequestDuration();
            options.EnableTryItOutByDefault();
            options.EnablePersistAuthorization();
            options.DocExpansion(DocExpansion.List);
        });

        app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
        app.MapGet("/docs", () => Results.Redirect("/swagger")).ExcludeFromDescription();

        app.MapControllers();

        return app;
    }
}
