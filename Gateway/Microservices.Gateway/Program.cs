using System.Threading.RateLimiting;
using Microservices.Common.Constants;
using Microsoft.AspNetCore.RateLimiting;
using Microservices.WebCore.Extensions;
using Microservices.WebCore.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Structured logging with WebCore Serilog setup
builder.Services.AddMicroserviceSerilog(builder.Configuration, "microservices-gateway");
builder.Services.AddHealthChecks();

// Edge Rate Limiting (Partitioned per Client IP to prevent single-client DoS)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("gateway-limiter", httpContext =>
    {
        var clientIp = httpContext.Request.Headers[AppHeaderNames.XForwardedFor].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(clientIp))
        {
            clientIp = clientIp.Split(',')[0].Trim();
        }
        else
        {
            clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        }

        return RateLimitPartition.GetSlidingWindowLimiter(
            clientIp,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromSeconds(1),
                SegmentsPerWindow = 2,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 20
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

// Register YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

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
