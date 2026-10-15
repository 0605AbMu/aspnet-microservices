using Microservices.WebCore.Extensions;
using Microservices.WebCore.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Structured logging with WebCore Serilog setup
builder.Services.AddMicroserviceSerilog(builder.Configuration, "microservices-gateway");
builder.Services.AddHealthChecks();

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

// Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");

    await next();
});

app.UseCors("GatewayCorsPolicy");

// Shared health check endpoints
app.MapMicroserviceHealthChecks();

// Start YARP Reverse Proxy
app.MapReverseProxy();

app.Run();
