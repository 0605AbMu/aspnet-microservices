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

    public static IndexBuilder<TEntity> HasSoftDeleteFilter<TEntity>(this IndexBuilder<TEntity> indexBuilder) =>
        indexBuilder.HasFilter(SoftDeleteSqlFilter);

    public static IndexBuilder HasSoftDeleteFilter(this IndexBuilder indexBuilder) =>
        indexBuilder.HasFilter(SoftDeleteSqlFilter);

    /// <summary>
    /// Applies standard EF Core model conventions:
    /// 1. MassTransit Inbox/Outbox entities
    /// 2. BaseEntity&lt;long&gt;.Id primary key and PostgreSQL IdentityAlways column
    /// 3. AggregateRoot&lt;long&gt; PostgreSQL xmin optimistic concurrency token
    /// 4. Combined ISoftDeletable (!IsDeleted) and ITenantEntity global query filters without model-cache leakage
    /// </summary>
    public static ModelBuilder ApplyModelConventions(this ModelBuilder modelBuilder, BaseDbContext dbContext)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;

            if (typeof(BaseEntity<long>).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasKey(nameof(BaseEntity<long>.Id));
                modelBuilder.Entity(clrType).Property(nameof(BaseEntity<long>.Id)).UseIdentityAlwaysColumn();
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

            if (typeof(ITenantEntity).IsAssignableFrom(clrType) && typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .HasIndex(nameof(ITenantEntity.TenantId), nameof(ISoftDeletable.IsDeleted));
            }
            else if (typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .HasIndex(nameof(ITenantEntity.TenantId));
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

        var isDisabledProp = Expression.Property(contextExpr, nameof(BaseDbContext.IsTenantFilterDisabled));
        var scopeProp = Expression.Property(contextExpr, nameof(BaseDbContext.CurrentScope));

        var globalConst = Expression.Constant(TenantScope.Global);
        var isGlobal = Expression.Equal(scopeProp, globalConst);

        var tenantIdProp = Expression.Property(contextExpr, nameof(BaseDbContext.CurrentTenantId));
        var entityTenantId = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        var matchTenant = Expression.Equal(entityTenantId, tenantIdProp);

        var combinedScopeFilter = Expression.OrElse(isGlobal, matchTenant);

        return Expression.OrElse(isDisabledProp, combinedScopeFilter);
    }
}
