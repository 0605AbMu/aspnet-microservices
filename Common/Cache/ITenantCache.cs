namespace Microservices.Common.Cache;

public interface ITenantCache
{
    Task<T> GetOrCreateAsync<T>(
        string keyTemplate,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? localTtl = null,
        TimeSpan? remoteTtl = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    Task SetAsync<T>(
        string keyTemplate,
        T value,
        TimeSpan? localTtl = null,
        TimeSpan? remoteTtl = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(string keyTemplate, CancellationToken cancellationToken = default);

    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);
}
