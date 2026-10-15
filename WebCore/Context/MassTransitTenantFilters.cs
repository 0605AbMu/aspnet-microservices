using MassTransit;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Microservices.WebCore.Context;

/// <summary>
/// Automatically propagates TenantId, DistrictId, RegionId, TenantScope, UserId, and CorrelationId
/// into outgoing MassTransit message headers.
/// </summary>
public class MassTransitTenantPublishFilter<T>(
    ITenantContext tenantContext,
    ICurrentUser currentUser) : IFilter<PublishContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("tenant-publish-filter");

    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        if (tenantContext.TenantId.HasValue)
            context.Headers.Set(AppHeaderNames.TenantId, tenantContext.TenantId.Value.ToString());

        if (tenantContext.DistrictId.HasValue)
            context.Headers.Set(AppHeaderNames.DistrictId, tenantContext.DistrictId.Value.ToString());

        if (tenantContext.RegionId.HasValue)
            context.Headers.Set(AppHeaderNames.RegionId, tenantContext.RegionId.Value.ToString());

        context.Headers.Set(AppHeaderNames.TenantScope, tenantContext.Scope.ToString());

        if (currentUser.UserId.HasValue)
            context.Headers.Set(AppHeaderNames.UserId, currentUser.UserId.Value.ToString());

        await next.Send(context);
    }
}

/// <summary>
/// Automatically propagates tenant context for direct Send messages.
/// </summary>
public class MassTransitTenantSendFilter<T>(
    ITenantContext tenantContext,
    ICurrentUser currentUser) : IFilter<SendContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("tenant-send-filter");

    public async Task Send(SendContext<T> context, IPipe<SendContext<T>> next)
    {
        if (tenantContext.TenantId.HasValue)
            context.Headers.Set(AppHeaderNames.TenantId, tenantContext.TenantId.Value.ToString());

        if (tenantContext.DistrictId.HasValue)
            context.Headers.Set(AppHeaderNames.DistrictId, tenantContext.DistrictId.Value.ToString());

        if (tenantContext.RegionId.HasValue)
            context.Headers.Set(AppHeaderNames.RegionId, tenantContext.RegionId.Value.ToString());

        context.Headers.Set(AppHeaderNames.TenantScope, tenantContext.Scope.ToString());

        if (currentUser.UserId.HasValue)
            context.Headers.Set(AppHeaderNames.UserId, currentUser.UserId.Value.ToString());

        await next.Send(context);
    }
}

/// <summary>
/// Extracts tenant context from incoming message headers and sets ITenantContext
/// via AsyncLocal scope for the duration of consumer execution.
/// </summary>
public class MassTransitTenantConsumeFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("tenant-consume-filter");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var tenantContext = context.GetPayload<IServiceProvider>().GetRequiredService<ITenantContext>();

        long? tenantId = TryGetLongHeader(context.Headers, AppHeaderNames.TenantId);
        long? districtId = TryGetLongHeader(context.Headers, AppHeaderNames.DistrictId);
        long? regionId = TryGetLongHeader(context.Headers, AppHeaderNames.RegionId);

        TenantScope? scope = null;
        if (context.Headers.TryGetHeader(AppHeaderNames.TenantScope, out var scopeObj) &&
            scopeObj is string scopeStr &&
            Enum.TryParse<TenantScope>(scopeStr, ignoreCase: true, out var parsedScope))
        {
            scope = parsedScope;
        }

        using (tenantContext.Change(tenantId, districtId, regionId, scope))
        {
            await next.Send(context);
        }
    }

    private static long? TryGetLongHeader(Headers headers, string key)
    {
        if (headers.TryGetHeader(key, out var val))
        {
            if (val is long l) return l;
            if (val is int i) return i;
            if (val is string s && long.TryParse(s, out var parsed)) return parsed;
        }
        return null;
    }
}
