namespace Microservices.Common.Context;

public enum TenantScope
{
    Tenant = 0,
    District = 1,
    Region = 2,
    Global = 3
}

public interface ITenantContext
{
    long? TenantId { get; }
    long? DistrictId { get; }
    long? RegionId { get; }
    TenantScope Scope { get; }

    bool HasTenant => TenantId.HasValue && TenantId.Value > 0;
    bool IsCrossTenant => Scope >= TenantScope.District || IsTenantFilterDisabled;

    /// <summary>
    /// Indicates whether tenant filtering is temporarily bypassed (e.g., background jobs or global reports).
    /// Soft-delete (IsDeleted = false) filter remains permanently active.
    /// </summary>
    bool IsTenantFilterDisabled { get; }

    /// <summary>
    /// Safely disables the tenant filter within the using-scope of the returned disposable token.
    /// </summary>
    IDisposable DisableTenantFilter();

    long ResolveTenantId(long? requestedTenantId = null) =>
        requestedTenantId is > 0 ? requestedTenantId.Value : (TenantId ?? 0);
}

public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();

    public long? TenantId => null;
    public long? DistrictId => null;
    public long? RegionId => null;
    public TenantScope Scope => TenantScope.Global;
    public bool IsTenantFilterDisabled => true;

    public IDisposable DisableTenantFilter() => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
