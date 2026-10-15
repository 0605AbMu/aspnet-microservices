using Microservices.Common.Constants;
using Microservices.Common.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microservices.WebCore.Extensions;

public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry tracing with OTLP exporter.
    /// </summary>
    public static IServiceCollection AddMicroserviceOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceOptions = configuration.GetSection(ConfigSections.Service).Get<ServiceOptions>() ?? new ServiceOptions();
        var otelOptions = configuration.GetSection(ConfigSections.OpenTelemetry).Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource.AddService(serviceOptions.Name, serviceVersion: serviceOptions.Version))
            .WithTracing(tracing =>
            {
                tracing
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
                        opts.Endpoint = new Uri(otelOptions.Endpoint);
                    });
            });

        return services;
    }
}
