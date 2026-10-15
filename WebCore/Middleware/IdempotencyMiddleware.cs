using System.Text;
using System.Text.Json;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Middleware;

/// <summary>
/// Idempotency Middleware.
/// Prevents duplicate POST/PUT requests when user double-clicks or retries.
/// Caches the response by Idempotency-Key in Redis for 2 minutes.
/// </summary>
public class IdempotencyMiddleware(
    RequestDelegate next,
    IDistributedCache cache,
    ITenantContext tenantContext,
    ILogger<IdempotencyMiddleware> logger)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(AppHeaderNames.IdempotencyKey, out var keyVal) || string.IsNullOrWhiteSpace(keyVal))
        {
            await next(context);
            return;
        }

        var key = keyVal.ToString().Trim();
        var tenantId = tenantContext.TenantId ?? 0;
        var cacheKey = string.Format(AppCacheKeys.IdempotencyKeyFormat, tenantId, context.Request.Path, key);

        var cachedData = await cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedData))
        {
            logger.LogInformation("Returning cached idempotent response for key {Key}", key);
            var cached = JsonSerializer.Deserialize<CachedHttpResponse>(cachedData);
            if (cached != null)
            {
                context.Response.StatusCode = cached.StatusCode;
                context.Response.ContentType = cached.ContentType;
                context.Response.Headers[AppHeaderNames.IdempotencyKey] = key;
                context.Response.Headers[AppHeaderNames.XCache] = "HIT-IDEMPOTENT";
                await context.Response.WriteAsync(cached.Body);
                return;
            }
        }

        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        try
        {
            await next(context);

            memoryStream.Position = 0;
            var responseBody = await new StreamReader(memoryStream, Encoding.UTF8).ReadToEndAsync();

            if (context.Response.StatusCode is >= 200 and < 500)
            {
                var cachedObj = new CachedHttpResponse
                {
                    StatusCode = context.Response.StatusCode,
                    ContentType = context.Response.ContentType ?? "application/json",
                    Body = responseBody
                };

                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = DefaultTtl
                };

                await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cachedObj), cacheOptions);
            }

            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private class CachedHttpResponse
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = "application/json";
        public string Body { get; set; } = string.Empty;
    }
}
