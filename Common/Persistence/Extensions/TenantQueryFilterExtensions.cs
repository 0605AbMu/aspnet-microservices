using System.Linq.Expressions;
using MassTransit;
using Microservices.Common.Context;
using Microservices.Common.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microservices.Common.Persistence.Extensions;

public static class TenantQueryFilterExtensions
{
    public const string SoftDeleteSqlFilter = "is_deleted = false";

    /// <summary>Shared PostgreSQL sequence that hands out blocks of primary keys (HiLo) to BaseEntity&lt;long&gt;.</summary>
    public const string IdSequenceName = "id_hilo_seq";

    /// <summary>Ids reserved per sequence round trip: one sequence call serves this many inserts.</summary>
    public const int IdSequenceBlockSize = 100;

    public static IndexBuilder<TEntity> HasSoftDeleteFilter<TEntity>(this IndexBuilder<TEntity> indexBuilder) =>
        indexBuilder.HasFilter(SoftDeleteSqlFilter);

    public static IndexBuilder HasSoftDeleteFilter(this IndexBuilder indexBuilder) =>
        indexBuilder.HasFilter(SoftDeleteSqlFilter);

    /// <summary>
    /// Applies standard EF Core model conventions:
    /// 1. MassTransit Inbox/Outbox entities
    /// 2. BaseEntity&lt;long&gt;.Id primary key, allocated client-side from a HiLo sequence (the Id is known before INSERT, so an entity and its outbox events are saved in one batch)
    /// 3. AggregateRoot&lt;long&gt; PostgreSQL xmin optimistic concurrency token
    /// 4. Combined ISoftDeletable (!IsDeleted) and ITenantEntity global query filters without model-cache leakage
    /// </summary>
    public static ModelBuilder ApplyModelConventions(this ModelBuilder modelBuilder, BaseDbContext dbContext)
    {
        modelBuilder.HasSequence<long>(IdSequenceName).StartsAt(1).IncrementsBy(IdSequenceBlockSize);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;

            if (typeof(BaseEntity<long>).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasKey(nameof(BaseEntity<long>.Id));
                modelBuilder.Entity(clrType).Property(nameof(BaseEntity<long>.Id)).UseHiLo(IdSequenceName);
            }

            if (typeof(AggregateRoot<long>).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            }

            var parameter = Expression.Parameter(clrType, "e");
            Expression? combinedFilter = null;

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                var isDeletedProp = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                var falseConst = Expression.Constant(false);
                combinedFilter = Expression.Equal(isDeletedProp, falseConst);
            }

            if (typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                var tenantFilter = BuildTenantFilter(parameter, dbContext);
                combinedFilter = combinedFilter is null
                    ? tenantFilter
                    : Expression.AndAlso(combinedFilter, tenantFilter);
            }

            if (combinedFilter is not null)
            {
                entityType.SetQueryFilter(Expression.Lambda(combinedFilter, parameter));
            }

            // Tenant-scoped list queries are "WHERE tenant_id = @t [AND NOT is_deleted] ORDER BY id DESC LIMIT n":
            // (tenant_id, id) serves the filter, the default sort (as a backward scan) and the LIMIT from a single
            // index range scan (a plain (tenant_id, is_deleted) index cannot, which makes PostgreSQL walk the PK
            // backwards through every other tenant's rows). Ascending on purpose: ids grow monotonically, so inserts
            // append to the end of each tenant's range and B-tree pages stay full (a DESC index fills them to ~50%).
            // Soft-deleted rows are excluded from the index (partial index).
            if (typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                var isSoftDeletable = typeof(ISoftDeletable).IsAssignableFrom(clrType);

                if (typeof(BaseEntity<long>).IsAssignableFrom(clrType))
                {
                    var index = modelBuilder.Entity(clrType)
                        .HasIndex(nameof(ITenantEntity.TenantId), nameof(BaseEntity<long>.Id));

                    if (isSoftDeletable)
                    {
                        index.HasSoftDeleteFilter();
                    }
                }
                else
                {
                    var index = modelBuilder.Entity(clrType).HasIndex(nameof(ITenantEntity.TenantId));

                    if (isSoftDeletable)
                    {
                        index.HasSoftDeleteFilter();
                    }
                }
            }
        }

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        return modelBuilder;
    }

    private static Expression BuildTenantFilter(
        ParameterExpression parameter,
        BaseDbContext dbContext)
    {
        var contextExpr = Expression.Constant(dbContext);

        // The bypass switch (Global scope / DisableTenantFilter) is inlined into the SQL as a constant instead of
        // being sent as bool parameters: "WHERE (@bypass OR @global OR tenant_id = @t)" cannot use the tenant index
        // once PostgreSQL settles on a generic plan, and forces a re-plan per execution otherwise.
        // With the constant, tenant mode compiles to "WHERE tenant_id = @t" and cross-tenant mode drops the predicate.
        // TenantModeCompiledQueryCacheKeyGenerator keys compiled queries by this switch, so the two shapes never mix.
        var bypassProp = Expression.Property(contextExpr, nameof(BaseDbContext.IsTenantFilterBypassed));
        var bypassConstant = Expression.Call(
            typeof(EF),
            nameof(EF.Constant),
            [typeof(bool)],
            bypassProp);

        var tenantIdProp = Expression.Property(contextExpr, nameof(BaseDbContext.CurrentTenantId));
        var entityTenantId = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        var matchTenant = Expression.Equal(entityTenantId, tenantIdProp);

        return Expression.OrElse(bypassConstant, matchTenant);
    }
}
