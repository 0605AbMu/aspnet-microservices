using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.WebCore.Extensions;

public static class CompressionExtensions
{
    /// <summary>
    /// Enables Brotli (primary) + Gzip (fallback) response compression.
    /// Brotli: Fastest level for low latency. Gzip (fallback): Fastest as well - CPU matters more than a few bytes.
    /// </summary>
    public static IServiceCollection AddMicroserviceCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes =
            [
                "application/json",
                "application/problem+json",
                "application/xml",
                "text/xml",
                "text/plain",
                "text/html",
                "text/css",
                "application/javascript",
                "text/javascript",
                "application/grpc-web",
                "application/wasm"
            ];
        });

        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            // Fastest: SmallestSize (gzip level 9) costs several times the CPU for a few percent smaller JSON.
            options.Level = CompressionLevel.Fastest;
        });

        return services;
    }

    public static IApplicationBuilder UseMicroserviceCompression(this IApplicationBuilder app)
    {
        app.UseResponseCompression();
        return app;
    }
}
