using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using StackExchange.Redis;

namespace Microservices.WebCore.Middleware;

/// <summary>
/// Idempotency Middleware.
/// Prevents duplicate POST/PUT requests when user double-clicks or retries.
/// Verifies request body payload hash to prevent key reuse with different parameters (Fintech standard).
/// Uses RecyclableMemoryStreamManager for response buffering.
/// Caches successful 2xx JSON responses (&lt; 1 MB) by Idempotency-Key in Redis for 2 minutes.
///
/// Redis cost per request: exactly 2 round trips, both atomic Lua scripts:
///   1. START  - "return the cached response if present, otherwise take the in-flight lock" (GET + SET NX in one step)
///   2. FINISH - "store the response (if cacheable) and release the lock if still owned" (SET + GET + DEL in one step)
/// Both keys share a hash tag, so the scripts are also valid on Redis Cluster.
/// </summary>
public class IdempotencyMiddleware
{
    private const int StatusReplay = 1;
    private const int StatusLockAcquired = 2;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LockRenewInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LockWait = TimeSpan.FromMilliseconds(500);
    private const int MaxCachedResponseBytes = 1024 * 1024;
    private const long MaxHashedBodyBytes = 1024 * 1024;

    private static readonly RecyclableMemoryStreamManager FallbackStreamManager = new();

    // KEYS[1]=response key, KEYS[2]=lock key, ARGV[1]=lock token, ARGV[2]=lock ttl ms
    private static readonly RedisScript StartScript = new(
        """
        local cached = redis.call('GET', KEYS[1])
        if cached then return {1, cached} end
        if redis.call('SET', KEYS[2], ARGV[1], 'NX', 'PX', ARGV[2]) then return {2} end
        return {3}
        """);

    // KEYS[1]=response key, KEYS[2]=lock key, ARGV[1]=lock token, ARGV[2]=response ('' = do not cache), ARGV[3]=cache ttl ms
    private static readonly RedisScript FinishScript = new(
        """
        if ARGV[2] ~= '' then redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3]) end
        if redis.call('GET', KEYS[2]) == ARGV[1] then redis.call('DEL', KEYS[2]) end
        return 1
        """);

    // KEYS[1]=lock key, ARGV[1]=lock token, ARGV[2]=lock ttl ms
    private static readonly RedisScript RenewScript = new(
        """
        if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('PEXPIRE', KEYS[1], ARGV[2]) end
        return 0
        """);

    private readonly RequestDelegate _next;
    private readonly IDatabase _db;
    private readonly ILogger<IdempotencyMiddleware> _logger;
    private readonly RecyclableMemoryStreamManager _streamManager;

    public IdempotencyMiddleware(
        RequestDelegate next,
        IConnectionMultiplexer redis,
        ILogger<IdempotencyMiddleware> logger,
        RecyclableMemoryStreamManager? memoryStreamManager = null)
    {
        _next = next;
        _db = redis.GetDatabase();
        _logger = logger;
        _streamManager = memoryStreamManager ?? FallbackStreamManager;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        if (!HttpMethods.IsPost(context.Request.Method) && !HttpMethods.IsPut(context.Request.Method))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(AppHeaderNames.IdempotencyKey, out var keyVal) || string.IsNullOrWhiteSpace(keyVal))
        {
            await _next(context);
            return;
        }

        var key = keyVal.ToString().Trim();
        var tenantId = tenantContext.TenantId ?? 0;

        // {…} is the Redis Cluster hash tag: response and lock keys land in the same slot.
        var tag = $"{Microservices.WebCore.Logging.AppLogContext.CurrentServiceName}:{tenantId}:{context.Request.Path}:{key}";
        RedisKey[] keys = [$"idem:{{{tag}}}:r", $"idem:{{{tag}}}:l"];

        var currentPayloadHash = await ComputeRequestPayloadHashAsync(context.Request, context.RequestAborted);
        var lockToken = Guid.NewGuid().ToString("N");

        // 1 round trip: replay the cached response, or take the in-flight lock. If another request holds the
        // lock, wait (like the previous lock-based implementation) up to LockWait for it to finish and replay.
        var deadline = DateTime.UtcNow + LockWait;
        while (true)
        {
            var (status, cachedBytes) = await StartAsync(keys, lockToken);

            if (status == StatusReplay)
            {
                await ReplayAsync(context, key, cachedBytes!, currentPayloadHash);
                return;
            }

            if (status == StatusLockAcquired)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline || context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogWarning("Concurrent idempotent request detected for key {Key}", key);
                await context.WriteErrorResponseAsync(StatusCodes.Status409Conflict, "request_in_progress");
                return;
            }

            try
            {
                await Task.Delay(Random.Shared.Next(35, 65), context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        // Long-running handlers keep the lock alive (every 20 s) just like the old watchdog; a request that ends
        // before the first tick (virtually all of them) only pays for creating and disposing the timer.
        using var renewal = new Timer(
            _ => _ = RenewLockAsync(keys[1], lockToken),
            null,
            LockRenewInterval,
            LockRenewInterval);

        var originalBodyStream = context.Response.Body;
        using var memoryStream = (RecyclableMemoryStream)_streamManager.GetStream();
        context.Response.Body = memoryStream;

        byte[]? responseToCache = null;
        try
        {
            await _next(context);

            // Only cache successful 2xx responses that are JSON and within safe size (< 1 MB) to protect LOH
            var isJson = context.Response.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? true;
            var isCacheableSize = memoryStream.Length <= MaxCachedResponseBytes;

            if (context.Response.StatusCode is >= 200 and < 300 && isJson && isCacheableSize)
            {
                responseToCache = JsonSerializer.SerializeToUtf8Bytes(
                    new CachedHttpResponse
                    {
                        StatusCode = context.Response.StatusCode,
                        ContentType = context.Response.ContentType ?? "application/json",
                        Body = Encoding.UTF8.GetString(memoryStream.GetReadOnlySequence()),
                        RequestPayloadHash = currentPayloadHash
                    },
                    HttpContextExtensions.SharedJsonOptions);
            }
        }
        finally
        {
            context.Response.Body = originalBodyStream;
            await FinishAsync(keys, lockToken, responseToCache);
        }

        memoryStream.Position = 0;
        await memoryStream.CopyToAsync(originalBodyStream);
    }

    private async Task<(int Status, byte[]? Cached)> StartAsync(RedisKey[] keys, string lockToken)
    {
        var result = (RedisResult[])(await EvalAsync(StartScript, keys, [lockToken, (long)LockTtl.TotalMilliseconds]))!;
        var status = (int)result[0];
        return (status, status == StatusReplay ? (byte[]?)result[1] : null);
    }

    private async Task FinishAsync(RedisKey[] keys, string lockToken, byte[]? response)
    {
        try
        {
            await EvalAsync(
                FinishScript,
                keys,
                [lockToken, response is null ? RedisValue.EmptyString : response, (long)CacheTtl.TotalMilliseconds]);
        }
        catch (Exception ex)
        {
            // Never mask the handler's own outcome (or exception). The lock simply expires after LockTtl.
            _logger.LogError(ex, "Failed to store idempotent response / release lock for {Key}", keys[1].ToString());
        }
    }

    private async Task RenewLockAsync(RedisKey lockKey, string lockToken)
    {
        try
        {
            await EvalAsync(RenewScript, [lockKey], [lockToken, (long)LockTtl.TotalMilliseconds]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extend idempotency lock {Key}", lockKey.ToString());
        }
    }

    private async Task<RedisResult> EvalAsync(RedisScript script, RedisKey[] keys, RedisValue[] values)
    {
        try
        {
            // EVALSHA keeps the payload tiny; the script is (re)loaded on the first call or after a Redis restart.
            return await _db.ScriptEvaluateAsync(script.Hash, keys, values);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("NOSCRIPT", StringComparison.Ordinal))
        {
            return await _db.ScriptEvaluateAsync(script.Text, keys, values);
        }
    }

    private async Task ReplayAsync(HttpContext context, string key, byte[] cachedBytes, string currentPayloadHash)
    {
        var cached = JsonSerializer.Deserialize<CachedHttpResponse>(cachedBytes, HttpContextExtensions.SharedJsonOptions);
        if (cached is null)
        {
            await _next(context);
            return;
        }

        if (!string.IsNullOrEmpty(cached.RequestPayloadHash) && cached.RequestPayloadHash != currentPayloadHash)
        {
            _logger.LogWarning("Idempotency-Key {Key} reuse detected with different payload hash", key);
            await context.WriteErrorResponseAsync(
                StatusCodes.Status409Conflict,
                "idempotency_payload_mismatch",
                "The Idempotency-Key was already used with a different request payload.");
            return;
        }

        _logger.LogInformation("Returning cached idempotent response for key {Key}", key);
        context.Response.StatusCode = cached.StatusCode;
        context.Response.ContentType = cached.ContentType;
        context.Response.Headers[AppHeaderNames.IdempotencyKey] = key;
        context.Response.Headers[AppHeaderNames.XCache] = "HIT-IDEMPOTENT";
        await context.Response.WriteAsync(cached.Body);
    }

    private static async Task<string> ComputeRequestPayloadHashAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ContentLength == 0)
        {
            return string.Empty;
        }

        // Large uploads are not buffered (to disk) and re-read just to be hashed; their length is still compared.
        if (request.ContentLength > MaxHashedBodyBytes)
        {
            return $"len:{request.ContentLength}";
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        var hashBytes = await SHA256.HashDataAsync(request.Body, cancellationToken);
        request.Body.Position = 0;

        return Convert.ToHexString(hashBytes);
    }

    private sealed class RedisScript
    {
        public RedisScript(string text)
        {
            Text = text;
            Hash = SHA1.HashData(Encoding.UTF8.GetBytes(text));
        }

        public string Text { get; }
        public byte[] Hash { get; }
    }

    private class CachedHttpResponse
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; } = "application/json";
        public string Body { get; set; } = string.Empty;
        public string? RequestPayloadHash { get; set; }
    }
}
