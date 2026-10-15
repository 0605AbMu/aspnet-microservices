namespace Microservices.Common.Context;

public enum TenantScope
{
    Tenant = 0,
    Global = 1
}

public interface ITenantContext
{
    long? TenantId { get; }
    TenantScope Scope { get; }

    bool HasTenant => TenantId.HasValue && TenantId.Value > 0;
    bool IsCrossTenant => Scope == TenantScope.Global || IsTenantFilterDisabled;

    /// <summary>
    /// Indicates whether tenant filtering is temporarily bypassed (e.g., background jobs or global reports).
    /// Soft-delete (IsDeleted = false) filter remains permanently active.
    /// </summary>
    bool IsTenantFilterDisabled { get; }

    /// <summary>
    /// Safely disables the tenant filter within the using-scope of the returned disposable token.
    /// </summary>
    IDisposable DisableTenantFilter();

    /// <summary>
    /// Temporarily changes the current tenant context (e.g. inside background workers or MassTransit consumers).
    /// Restores the previous context when the returned token is disposed.
    /// </summary>
    IDisposable Change(long? tenantId, TenantScope? scope = null);

    long ResolveTenantId(long? requestedTenantId = null) =>
        requestedTenantId is > 0 ? requestedTenantId.Value : (TenantId ?? 0);
}

public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();

    public long? TenantId => null;
    public TenantScope Scope => TenantScope.Global;
    public bool IsTenantFilterDisabled => true;

    public IDisposable DisableTenantFilter() => NullDisposable.Instance;
    public IDisposable Change(long? tenantId, TenantScope? scope = null) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}
