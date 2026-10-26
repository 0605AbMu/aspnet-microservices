using System.Threading.RateLimiting;
using Microservices.Common.Constants;
using Microsoft.AspNetCore.RateLimiting;
using Microservices.WebCore.Extensions;
using Microservices.WebCore.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Structured logging with WebCore Serilog setup
builder.Services.AddMicroserviceSerilog(builder.Configuration, "microservices-gateway");
builder.Services.AddHealthChecks();
builder.Services.AddMicroserviceForwardedHeaders();

// Edge Rate Limiting (Partitioned per Client IP to prevent single-client DoS).
// Limits are configurable ("Gateway:RateLimit"); the defaults are deliberately generous because one client IP is
// often many users (corporate NAT, mobile carrier NAT, a load balancer or another backend calling the API).
var gatewayRateLimit = builder.Configuration.GetSection("Gateway:RateLimit").Get<GatewayRateLimitOptions>()
    ?? new GatewayRateLimitOptions();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("gateway-limiter", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? httpContext.Request.Headers[AppHeaderNames.XForwardedFor].FirstOrDefault()?.Split(',')[0].Trim()
            ?? "unknown-ip";

        return RateLimitPartition.GetSlidingWindowLimiter(
            clientIp,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = gatewayRateLimit.PermitLimit,
                Window = TimeSpan.FromSeconds(gatewayRateLimit.WindowSeconds),
                SegmentsPerWindow = gatewayRateLimit.SegmentsPerWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                // Queued requests only add latency (p95 of ~1 s was measured at the old limit); reject instead.
                QueueLimit = gatewayRateLimit.QueueLimit
            });
    });
});

// Configure CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000", "http://localhost:5173", "http://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("GatewayCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Register YARP Reverse Proxy with tuned SocketsHttpHandler
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .ConfigureHttpClient((context, handler) =>
    {
        handler.EnableMultipleHttp2Connections = true;
        handler.AutomaticDecompression = System.Net.DecompressionMethods.None;
        handler.PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2);
        handler.PooledConnectionLifetime = TimeSpan.FromMinutes(15);
        handler.KeepAlivePingDelay = TimeSpan.FromSeconds(30);
        handler.KeepAlivePingTimeout = TimeSpan.FromSeconds(5);
    });

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();

// Security Headers Middleware (using indexer to prevent duplicates)
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";

    await next();
});

app.UseCors("GatewayCorsPolicy");
app.UseRateLimiter();

// Shared health check endpoints
app.MapMicroserviceHealthChecks();

// Start YARP Reverse Proxy
app.MapReverseProxy();

app.Run();

internal sealed class GatewayRateLimitOptions
{
    public int PermitLimit { get; set; } = 1000;
    public int WindowSeconds { get; set; } = 1;
    public int SegmentsPerWindow { get; set; } = 2;
    public int QueueLimit { get; set; } = 0;
}
