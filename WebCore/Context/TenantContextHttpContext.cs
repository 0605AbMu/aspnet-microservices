using System.Security.Claims;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microsoft.AspNetCore.Http;

namespace Microservices.WebCore.Context;

/// <summary>
/// Reads tenant context from JWT claims in HttpContext.
/// Supports hierarchical tenancy: Tenant → District → Region → Global.
/// Supports DisableTenantFilter() for background tasks or cross-tenant queries.
/// Registered as Scoped — per-request lifetime.
/// </summary>
public class TenantContextHttpContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private static readonly System.Threading.AsyncLocal<TenantOverrideData?> _override = new();
    private bool isTenantFilterDisabled;

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public long? TenantId =>
        _override.Value?.TenantId ?? (long.TryParse(User?.FindFirstValue(AppClaimTypes.TenantId), out var id) ? id : null);

    public long? DistrictId =>
        _override.Value?.DistrictId ?? (long.TryParse(User?.FindFirstValue(AppClaimTypes.DistrictId), out var id) ? id : null);

    public long? RegionId =>
        _override.Value?.RegionId ?? (long.TryParse(User?.FindFirstValue(AppClaimTypes.RegionId), out var id) ? id : null);

    public TenantScope Scope =>
        _override.Value?.Scope ?? (Enum.TryParse<TenantScope>(User?.FindFirstValue(AppClaimTypes.TenantScope), ignoreCase: true, out var scope)
            ? scope
            : TenantScope.Tenant);

    public bool IsTenantFilterDisabled => isTenantFilterDisabled;

    public IDisposable DisableTenantFilter()
    {
        isTenantFilterDisabled = true;
        return new FilterScopeToken(() => isTenantFilterDisabled = false);
    }

    public IDisposable Change(long? tenantId, long? districtId = null, long? regionId = null, TenantScope? scope = null)
    {
        var previous = _override.Value;
        var resolvedScope = scope ?? (tenantId.HasValue ? TenantScope.Tenant : TenantScope.Global);
        _override.Value = new TenantOverrideData(tenantId, districtId, regionId, resolvedScope);
        return new FilterScopeToken(() => _override.Value = previous);
    }

    private record TenantOverrideData(long? TenantId, long? DistrictId, long? RegionId, TenantScope Scope);

    private sealed class FilterScopeToken(Action onDispose) : IDisposable
    {
        private Action? onDispose = onDispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref onDispose, null)?.Invoke();
        }
    }
}
