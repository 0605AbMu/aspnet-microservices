using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Microservices.Common.Persistence;

/// <summary>
/// Adds the tenant-filter bypass switch to the compiled-query cache key.
/// The tenant query filter inlines the switch as a SQL constant (see TenantQueryFilterExtensions), so a query
/// compiled for tenant mode must never be reused for cross-tenant mode (and vice versa).
/// </summary>
public sealed class TenantModeCompiledQueryCacheKeyGenerator(
    CompiledQueryCacheKeyGeneratorDependencies dependencies,
    RelationalCompiledQueryCacheKeyGeneratorDependencies relationalDependencies)
    : RelationalCompiledQueryCacheKeyGenerator(dependencies, relationalDependencies)
{
    public override object GenerateCacheKey(Expression query, bool async)
    {
        var bypassed = Dependencies.CurrentContext.Context is BaseDbContext { IsTenantFilterBypassed: true };
        return (base.GenerateCacheKey(query, async), bypassed);
    }
}
