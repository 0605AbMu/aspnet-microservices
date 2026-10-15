using Microservices.Common.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Microservices.Common.Persistence;

/// <summary>
/// Shared base DbContext that automatically configures MassTransit Outbox entities,
/// BaseEntity&lt;long&gt; IdentityAlways keys, AggregateRoot&lt;long&gt; RowVersion, and ISoftDeletable query filters.
/// </summary>
public abstract class BaseDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyModelConventions();
    }
}
