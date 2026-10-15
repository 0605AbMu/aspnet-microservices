using Microservices.Common.Lock;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Microservices.WebCore.Lock;

/// <summary>
/// Redis Distributed Lock implementation.
/// Uses StackExchange.Redis atomic LockTake / LockRelease to prevent race conditions.
/// </summary>
public class RedisDistributedLock(
    IConnectionMultiplexer redis,
    ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    private readonly IDatabase database = redis.GetDatabase();

    public async Task<IAsyncDisposable?> TryAcquireLockAsync(
        string resourceKey,
        TimeSpan timeout,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        var lockValue = Guid.NewGuid().ToString("N");
        var lockKey = $"lock:{resourceKey}";
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            var acquired = await database.LockTakeAsync(lockKey, lockValue, expiry);
            if (acquired)
            {
                logger.LogDebug("Acquired distributed lock for key {Key}", lockKey);
                return new LockReleaser(database, lockKey, lockValue, logger);
            }

            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        logger.LogWarning("Failed to acquire distributed lock for key {Key} within timeout {Timeout}",
            lockKey, timeout);
        return null;
    }

    public async Task<bool> IsLockedAsync(string resourceKey, CancellationToken cancellationToken = default)
    {
        var lockKey = $"lock:{resourceKey}";
        var val = await database.LockQueryAsync(lockKey);
        return !val.IsNull;
    }

    private sealed class LockReleaser(
        IDatabase database,
        string lockKey,
        string lockValue,
        ILogger logger) : IAsyncDisposable
    {
        private int released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                var success = await database.LockReleaseAsync(lockKey, lockValue);
                if (success)
                    logger.LogDebug("Released distributed lock for key {Key}", lockKey);
                else
                    logger.LogWarning("Failed or lock expired before release for key {Key}", lockKey);
            }
        }
    }
}
