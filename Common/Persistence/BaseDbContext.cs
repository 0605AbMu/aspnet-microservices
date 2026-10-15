using Microservices.Common.Context;
using Microservices.Common.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Microservices.Common.Persistence;

/// <summary>
/// Shared base DbContext that automatically configures MassTransit Outbox entities,
/// BaseEntity&lt;long&gt; IdentityAlways keys, PostgreSQL xmin optimistic concurrency,
/// and combined ISoftDeletable + ITenantEntity global query filters without model-cache leakage.
/// </summary>
public abstract class BaseDbContext(DbContextOptions options, ITenantContext? tenantContext = null) : DbContext(options)
{
    public ITenantContext TenantContext { get; } = tenantContext ?? NullTenantContext.Instance;

    public long? CurrentTenantId => TenantContext.TenantId;
    public TenantScope CurrentScope => TenantContext.Scope;
    public bool IsTenantFilterDisabled => TenantContext.IsTenantFilterDisabled;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyModelConventions(this);
    }
}
