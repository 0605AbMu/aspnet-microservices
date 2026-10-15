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
    private bool isTenantFilterDisabled;

    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public long? TenantId
    {
        get
        {
            var raw = User?.FindFirstValue(AppClaimTypes.TenantId) ?? User?.FindFirstValue(AppClaimTypes.InstitutionId);
            return long.TryParse(raw, out var id) ? id : null;
        }
    }

    public long? InstitutionId => TenantId;

    public long? DistrictId
    {
        get
        {
            var raw = User?.FindFirstValue(AppClaimTypes.DistrictId);
            return long.TryParse(raw, out var id) ? id : null;
        }
    }

    public long? RegionId
    {
        get
        {
            var raw = User?.FindFirstValue(AppClaimTypes.RegionId);
            return long.TryParse(raw, out var id) ? id : null;
        }
    }

    public TenantScope Scope
    {
        get
        {
            var raw = User?.FindFirstValue(AppClaimTypes.TenantScope);
            return Enum.TryParse<TenantScope>(raw, ignoreCase: true, out var scope)
                ? scope
                : TenantScope.Tenant;
        }
    }

    public bool IsTenantFilterDisabled => isTenantFilterDisabled;

    public IDisposable DisableTenantFilter()
    {
        isTenantFilterDisabled = true;
        return new FilterScopeToken(() => isTenantFilterDisabled = false);
    }

    private sealed class FilterScopeToken(Action onDispose) : IDisposable
    {
        private Action? onDispose = onDispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref onDispose, null)?.Invoke();
        }
    }
}
