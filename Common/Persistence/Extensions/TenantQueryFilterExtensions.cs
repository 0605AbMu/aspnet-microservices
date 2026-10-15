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
    /// 3. AggregateRoot&lt;long&gt;.RowVersion optimistic concurrency token
    /// 4. Global query filter (!IsDeleted) on all ISoftDeletable entities
    /// </summary>
    public static ModelBuilder ApplyModelConventions(this ModelBuilder modelBuilder)
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
                modelBuilder.Entity(clrType).Property(nameof(AggregateRoot<long>.RowVersion)).IsRowVersion();
            }

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                var parameter = Expression.Parameter(clrType, "e");
                var property = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
                var falseConstant = Expression.Constant(false);
                var filter = Expression.Lambda(Expression.Equal(property, falseConstant), parameter);
                entityType.SetQueryFilter(filter);
            }
        }

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        return modelBuilder;
    }

    /// <summary>
    /// Applies hierarchical multi-tenancy global query filter on all ITenantEntity types.
    /// Respects TenantScope (Tenant, District, Region, Global) and DisableTenantFilter().
    /// </summary>
    public static ModelBuilder ApplyTenantQueryFilters(
        this ModelBuilder modelBuilder,
        ITenantContext tenantContext)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var filter = BuildTenantFilter(entityType.ClrType, parameter, tenantContext);
                entityType.SetQueryFilter(filter);
            }
        }

        return modelBuilder;
    }

    private static LambdaExpression BuildTenantFilter(
        Type clrType,
        ParameterExpression parameter,
        ITenantContext tenantContext)
    {
        var contextExpr = Expression.Constant(tenantContext);

        var isDisabledProp = Expression.Property(contextExpr, nameof(ITenantContext.IsTenantFilterDisabled));
        var scopeProp = Expression.Property(contextExpr, nameof(ITenantContext.Scope));

        var globalConst = Expression.Constant(TenantScope.Global);
        var isGlobal = Expression.Equal(scopeProp, globalConst);

        var regionConst = Expression.Constant(TenantScope.Region);
        var isRegion = Expression.Equal(scopeProp, regionConst);
        var regionIdProp = Expression.Property(contextExpr, nameof(ITenantContext.RegionId));
        var entityRegionId = Expression.Property(parameter, nameof(ITenantEntity.RegionId));
        var matchRegion = Expression.AndAlso(isRegion, Expression.Equal(entityRegionId, regionIdProp));

        var districtConst = Expression.Constant(TenantScope.District);
        var isDistrict = Expression.Equal(scopeProp, districtConst);
        var districtIdProp = Expression.Property(contextExpr, nameof(ITenantContext.DistrictId));
        var entityDistrictId = Expression.Property(parameter, nameof(ITenantEntity.DistrictId));
        var matchDistrict = Expression.AndAlso(isDistrict, Expression.Equal(entityDistrictId, districtIdProp));

        var tenantIdProp = Expression.Property(contextExpr, nameof(ITenantContext.TenantId));
        var entityTenantId = Expression.Property(parameter, nameof(ITenantEntity.TenantId));
        var matchTenant = Expression.Equal(entityTenantId, tenantIdProp);

        var combinedScopeFilter = Expression.OrElse(
            isGlobal,
            Expression.OrElse(matchRegion, Expression.OrElse(matchDistrict, matchTenant)));

        var finalBody = Expression.OrElse(isDisabledProp, combinedScopeFilter);

        return Expression.Lambda(finalBody, parameter);
    }
}
