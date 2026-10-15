using Microsoft.AspNetCore.RateLimiting;
using Microservices.WebCore.Extensions;
using Microservices.WebCore.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Structured logging with WebCore Serilog setup
builder.Services.AddMicroserviceSerilog(builder.Configuration, "microservices-gateway");
builder.Services.AddHealthChecks();

// Edge Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("gateway-limiter", opt =>
    {
        opt.PermitLimit = 200;
        opt.Window = TimeSpan.FromSeconds(1);
        opt.QueueLimit = 50;
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
