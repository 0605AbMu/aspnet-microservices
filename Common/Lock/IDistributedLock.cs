namespace Microservices.Common.Lock;

public interface IDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireLockAsync(
        string resourceKey,
        TimeSpan timeout,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    Task<bool> IsLockedAsync(string resourceKey, CancellationToken cancellationToken = default);
}
