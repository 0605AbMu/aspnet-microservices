using Microservices.Common.Context;
using Microservices.Common.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Microservices.Common.Persistence;

/// <summary>
/// Shared base DbContext that automatically configures MassTransit Outbox entities,
/// BaseEntity&lt;long&gt; HiLo keys, PostgreSQL xmin optimistic concurrency,
/// and combined ISoftDeletable + ITenantEntity global query filters without model-cache leakage.
/// </summary>
public abstract class BaseDbContext(DbContextOptions options, ITenantContext? tenantContext = null) : DbContext(options)
{
    public ITenantContext TenantContext { get; } = tenantContext ?? NullTenantContext.Instance;

    public long? CurrentTenantId => TenantContext.TenantId;
    public TenantScope CurrentScope => TenantContext.Scope;
    public bool IsTenantFilterDisabled => TenantContext.IsTenantFilterDisabled;

    /// <summary>
    /// True when the tenant predicate is skipped (Global scope or DisableTenantFilter()).
    /// Inlined as a SQL constant by the tenant query filter; part of the compiled-query cache key.
    /// </summary>
    public bool IsTenantFilterBypassed => IsTenantFilterDisabled || CurrentScope == TenantScope.Global;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Always applied here (not in DI registration) so a derived context can never run with the inlined
        // tenant switch and the default cache key, which would reuse a query compiled for another tenant mode.
        optionsBuilder.ReplaceService<ICompiledQueryCacheKeyGenerator, TenantModeCompiledQueryCacheKeyGenerator>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyModelConventions(this);
    }
}
