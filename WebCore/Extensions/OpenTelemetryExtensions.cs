using Microservices.Common.Constants;
using Microservices.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microservices.WebCore.Extensions;

public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry tracing and metrics with OTLP exporter,
    /// including ASP.NET Core, HttpClient, MassTransit, and Npgsql instrumentation.
    /// </summary>
    public static IServiceCollection AddMicroserviceOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceOptions = configuration.GetSection(ConfigSections.Service).Get<ServiceOptions>() ?? new ServiceOptions();
        var otelOptions = configuration.GetSection(ConfigSections.OpenTelemetry).Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();
        var otelEndpoint = new Uri(otelOptions.Endpoint);

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource.AddService(serviceOptions.Name, serviceVersion: serviceOptions.Version))
            .WithTracing(tracing =>
            {
                var samplingRatio = Math.Clamp(otelOptions.SamplingRatio, 0.0, 1.0);
                tracing
                    .SetSampler(new TraceIdRatioBasedSampler(samplingRatio))
                    .AddSource("MassTransit")
                    .AddSource("Npgsql")
                    .AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/health") &&
                            !ctx.Request.Path.StartsWithSegments("/metrics") &&
                            !ctx.Request.Path.StartsWithSegments("/.well-known");
                    })
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = otelEndpoint;
                    });
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http")
                    .AddOtlpExporter(opts =>
                    {
                        opts.Endpoint = otelEndpoint;
                    });
            });

        return services;
    }
}
