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
                return new LockReleaser(database, lockKey, lockValue, expiry, logger);
            }

            try
            {
                var jitterMs = Random.Shared.Next(35, 65);
                await Task.Delay(jitterMs, cancellationToken);
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

    private sealed class LockReleaser : IAsyncDisposable
    {
        private readonly IDatabase _database;
        private readonly string _lockKey;
        private readonly string _lockValue;
        private readonly TimeSpan _expiry;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task? _renewalTask;
        private int _released;

        public LockReleaser(IDatabase database, string lockKey, string lockValue, TimeSpan expiry, ILogger logger)
        {
            _database = database;
            _lockKey = lockKey;
            _lockValue = lockValue;
            _expiry = expiry;
            _logger = logger;

            var renewalInterval = TimeSpan.FromMilliseconds(Math.Max(1000, expiry.TotalMilliseconds / 2));
            _renewalTask = StartAutoRenewalAsync(renewalInterval, _cts.Token);
        }

        private async Task StartAutoRenewalAsync(TimeSpan interval, CancellationToken ct)
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    var extended = await _database.LockExtendAsync(_lockKey, _lockValue, _expiry);
                    if (!extended)
                    {
                        _logger.LogWarning("Failed to extend distributed lock for key {Key}", _lockKey);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation on disposal
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _cts.Cancel();
                if (_renewalTask is not null)
                {
                    try { await _renewalTask; } catch { }
                }
                _cts.Dispose();

                var success = await _database.LockReleaseAsync(_lockKey, _lockValue);
                if (success)
                    _logger.LogDebug("Released distributed lock for key {Key}", _lockKey);
                else
                    _logger.LogWarning("Failed or lock expired before release for key {Key}", _lockKey);
            }
        }
    }
}
