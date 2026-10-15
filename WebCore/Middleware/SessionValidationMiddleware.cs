using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Middleware;

/// <summary>
/// Session Validation Middleware.
/// Validates token revocation status using L1 Memory Cache (15-30s) + Redis.
/// Rejects revoked sessions immediately with 401 Unauthorized ("token_revoked").
/// </summary>
public class SessionValidationMiddleware(
    RequestDelegate next,
    IMemoryCache memoryCache,
    IDistributedCache distributedCache,
    ILogger<SessionValidationMiddleware> logger)
{
    private static readonly TimeSpan L1Ttl = TimeSpan.FromSeconds(20);

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        if (currentUser.IsAuthenticated && currentUser.UserId.HasValue)
        {
            var userId = currentUser.UserId.Value;
            var sessionId = currentUser.SessionId;
            var securityStamp = currentUser.SecurityStamp;

            if (!string.IsNullOrEmpty(sessionId) || !string.IsNullOrEmpty(securityStamp))
            {
                var isRevoked = await CheckIsRevokedAsync(userId, sessionId, securityStamp);
                if (isRevoked)
                {
                    logger.LogWarning("Session/Token revoked for user {UserId}. SessionId={SessionId}",
                        userId, sessionId);

                    await context.WriteErrorResponseAsync(StatusCodes.Status401Unauthorized, "token_revoked");
                    return;
                }
            }
        }

        await next(context);
    }

    private async Task<bool> CheckIsRevokedAsync(long userId, string? sessionId, string? securityStamp)
    {
        var tokenIdentifier = sessionId ?? securityStamp ?? string.Empty;
        var l1Key = string.Format(AppCacheKeys.SessionRevokedL1Format, userId, tokenIdentifier);

        // 1. Check L1 In-Memory Cache (0.001ms)
        if (memoryCache.TryGetValue(l1Key, out bool isRevokedInMemory))
        {
            return isRevokedInMemory;
        }

        // 2. Check L2 Redis Cache
        var redisKey = string.Format(AppCacheKeys.SessionRevokedL2Format, userId, tokenIdentifier);
        var redisVal = await distributedCache.GetStringAsync(redisKey);
        var isRevoked = !string.IsNullOrEmpty(redisVal);

        // Store in L1 for 20 seconds
        memoryCache.Set(l1Key, isRevoked, L1Ttl);

        return isRevoked;
    }
}
