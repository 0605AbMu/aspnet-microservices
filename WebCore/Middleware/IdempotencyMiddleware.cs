using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microservices.Common.Lock;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace Microservices.WebCore.Middleware;

/// <summary>
/// Idempotency Middleware.
/// Prevents duplicate POST/PUT requests when user double-clicks or retries.
/// Uses atomic distributed lock to prevent in-flight parallel race conditions.
/// Verifies request body payload hash to prevent key reuse with different parameters (Fintech standard).
/// Uses RecyclableMemoryStreamManager for zero-allocation response buffering.
/// Caches successful 2xx responses by Idempotency-Key in Redis for 2 minutes.
/// </summary>
public class IdempotencyMiddleware(
    RequestDelegate next,
    IDistributedCache cache,
    IDistributedLock distributedLock,
    ILogger<IdempotencyMiddleware> logger,
    RecyclableMemoryStreamManager? memoryStreamManager = null)
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(2);
    private static readonly RecyclableMemoryStreamManager FallbackStreamManager = new();
    private readonly RecyclableMemoryStreamManager _streamManager = memoryStreamManager ?? FallbackStreamManager;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
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
        var currentPayloadHash = await ComputeRequestPayloadHashAsync(context.Request);

        var cachedData = await cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedData))
        {
            var cached = JsonSerializer.Deserialize<CachedHttpResponse>(cachedData);
            if (cached != null)
            {
                if (!string.IsNullOrEmpty(cached.RequestPayloadHash) && cached.RequestPayloadHash != currentPayloadHash)
                {
                    logger.LogWarning("Idempotency-Key {Key} reuse detected with different payload hash", key);
                    await context.WriteErrorResponseAsync(
                        StatusCodes.Status409Conflict,
                        "idempotency_payload_mismatch",
                        "The Idempotency-Key was already used with a different request payload.");
                    return;
                }

                logger.LogInformation("Returning cached idempotent response for key {Key}", key);
                context.Response.StatusCode = cached.StatusCode;
                context.Response.ContentType = cached.ContentType;
                context.Response.Headers[AppHeaderNames.IdempotencyKey] = key;
                context.Response.Headers[AppHeaderNames.XCache] = "HIT-IDEMPOTENT";
                await context.Response.WriteAsync(cached.Body);
                return;
            }
        }

        // Acquire distributed lock to prevent concurrent execution of the same key
        var lockKey = $"idempotency:{tenantId}:{key}";
        await using var lockHandle = await distributedLock.TryAcquireLockAsync(
            lockKey,
            timeout: TimeSpan.FromMilliseconds(500),
            expiry: TimeSpan.FromSeconds(60),
            context.RequestAborted);

        if (lockHandle is null)
        {
            logger.LogWarning("Concurrent idempotent request detected for key {Key}", key);
            await context.WriteErrorResponseAsync(
                StatusCodes.Status409Conflict,
                "request_in_progress");
            return;
        }

        // Re-check cache in case a previous request completed while waiting for lock
        cachedData = await cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedData))
        {
            var cached = JsonSerializer.Deserialize<CachedHttpResponse>(cachedData);
            if (cached != null)
            {
                if (!string.IsNullOrEmpty(cached.RequestPayloadHash) && cached.RequestPayloadHash != currentPayloadHash)
                {
                    logger.LogWarning("Idempotency-Key {Key} reuse detected with different payload hash", key);
                    await context.WriteErrorResponseAsync(
                        StatusCodes.Status409Conflict,
                        "idempotency_payload_mismatch",
                        "The Idempotency-Key was already used with a different request payload.");
                    return;
                }

                context.Response.StatusCode = cached.StatusCode;
                context.Response.ContentType = cached.ContentType;
                context.Response.Headers[AppHeaderNames.IdempotencyKey] = key;
                context.Response.Headers[AppHeaderNames.XCache] = "HIT-IDEMPOTENT";
                await context.Response.WriteAsync(cached.Body);
                return;
            }
        }

        var originalBodyStream = context.Response.Body;
        using var memoryStream = _streamManager.GetStream();
        context.Response.Body = memoryStream;

        try
        {
            await next(context);

            memoryStream.Position = 0;
            var responseBody = await new StreamReader(memoryStream, Encoding.UTF8).ReadToEndAsync();

            // Only cache successful 2xx responses (allow clients to retry after errors)
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                var cachedObj = new CachedHttpResponse
                {
                    StatusCode = context.Response.StatusCode,
                    ContentType = context.Response.ContentType ?? "application/json",
                    Body = responseBody,
                    RequestPayloadHash = currentPayloadHash
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

    private static async Task<string> ComputeRequestPayloadHashAsync(HttpRequest request)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(request.Body);
        request.Body.Position = 0;

        return Convert.ToHexString(hashBytes);
    }

    private class CachedHttpResponse
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = "application/json";
        public string Body { get; set; } = string.Empty;
        public string? RequestPayloadHash { get; set; }
    }
}
