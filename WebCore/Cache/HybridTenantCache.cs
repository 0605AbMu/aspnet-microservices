using Microservices.Common.Cache;
using Microservices.Common.Context;
using Microsoft.Extensions.Caching.Hybrid;

namespace Microservices.WebCore.Cache;

/// <summary>
/// Tenant-aware HybridCache wrapper (L1 In-Memory + L2 Redis).
/// All cache keys are automatically prefixed with TenantId to prevent cross-tenant cache leaks.
/// Key format: {tenant_id}:{service}:{entity}:{id}
/// Uses HybridCache.GetOrCreateAsync — single-flight (Lock-on-Read) prevents cache stampede.
/// </summary>
public class HybridTenantCache(HybridCache hybridCache, ITenantContext tenantContext) : ITenantCache
{
    public async Task<T> GetOrCreateAsync<T>(
        string keyTemplate,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan? localTtl = null,
        TimeSpan? remoteTtl = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(keyTemplate);
        var tenantTags = tags?.Select(BuildTag).ToList();

        var options = new HybridCacheEntryOptions
        {
            LocalCacheExpiration = localTtl ?? TimeSpan.FromSeconds(30),
            Expiration = remoteTtl ?? TimeSpan.FromMinutes(5)
        };

        return await hybridCache.GetOrCreateAsync(key, factory, options, tenantTags, cancellationToken);
    }

    public async Task SetAsync<T>(
        string keyTemplate,
        T value,
        TimeSpan? localTtl = null,
        TimeSpan? remoteTtl = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(keyTemplate);
        var tenantTags = tags?.Select(BuildTag).ToList();

        var options = new HybridCacheEntryOptions
        {
            LocalCacheExpiration = localTtl ?? TimeSpan.FromSeconds(30),
            Expiration = remoteTtl ?? TimeSpan.FromMinutes(5)
        };

        await hybridCache.SetAsync(key, value, options, tenantTags, cancellationToken);
    }

    public async Task RemoveAsync(string keyTemplate, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(keyTemplate);
        await hybridCache.RemoveAsync(key, cancellationToken);
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        var tenantTag = BuildTag(tag);
        await hybridCache.RemoveByTagAsync(tenantTag, cancellationToken);
    }

    private string BuildTag(string tag) => $"{GetTenantPrefix()}:{tag}";

    private string BuildKey(string keyTemplate)
    {
        var normalized = keyTemplate.StartsWith("{0}:", StringComparison.Ordinal)
            ? keyTemplate[4..]
            : keyTemplate;
        var prefix = GetTenantPrefix();
        return $"{prefix}:{normalized}";
    }

    private string GetTenantPrefix()
    {
        if (tenantContext.IsCrossTenant)
            return $"scope:{tenantContext.Scope.ToString().ToLowerInvariant()}:{tenantContext.RegionId ?? 0}:{tenantContext.DistrictId ?? 0}";

        return $"tenant:{tenantContext.TenantId ?? 0}";
    }
}
