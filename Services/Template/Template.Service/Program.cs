using Microservices.Template.Service.Extensions;
using Microservices.Template.Service.Grpc;
using Microservices.WebCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Register shared infrastructure (Auth, HybridCache, OTel, Compression, RateLimiting, Logging)
builder.Services.AddWebCore(builder.Configuration, "template-service");

// Register Template infrastructure (PostgreSQL DbContextPool, MassTransit Outbox, gRPC Code-First)
builder.Services.AddTemplateInfrastructure(builder.Configuration);

// Register Template domain application services
builder.Services.AddTemplateApplicationServices();

var app = builder.Build();

// Shared middleware pipeline (CorrelationId, Serilog, Exception handling, RateLimiting, OpenAPI)
app.UseWebCore();

// Map gRPC Code-First endpoints
app.MapGrpcService<TemplateGrpcService>();

app.Run();
