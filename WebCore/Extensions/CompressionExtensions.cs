using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.WebCore.Extensions;

public static class CompressionExtensions
{
    /// <summary>
    /// Enables Brotli (primary) + Gzip (fallback) response compression.
    /// Brotli: Fastest level for low latency. Gzip: SmallestSize for maximum compression.
    /// </summary>
    public static IServiceCollection AddMicroserviceCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            [
                "application/json",
                "text/plain",
                "text/html",
                "text/css",
                "application/javascript"
            ]);
        });

        services.Configure<BrotliCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.SmallestSize;
        });

        return services;
    }

    public static IApplicationBuilder UseMicroserviceCompression(this IApplicationBuilder app)
    {
        app.UseResponseCompression();
        return app;
    }
}
