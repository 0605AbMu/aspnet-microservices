using Grpc.Core;
using Grpc.Core.Interceptors;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Http;

namespace Microservices.WebCore.Interceptors;

/// <summary>
/// Outgoing gRPC client interceptor.
/// Automatically propagates: traceparent, X-Correlation-Id, X-Tenant-Id, X-User-Id, Authorization, and Timeout deadline.
/// </summary>
public class GrpcClientContextInterceptor(
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IHttpContextAccessor httpContextAccessor) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var enriched = EnrichContext(context);
        return continuation(request, enriched);
    }

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var enriched = EnrichContext(context);
        return continuation(request, enriched);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        var enriched = EnrichContext(context);
        return continuation(request, enriched);
    }

    private ClientInterceptorContext<TRequest, TResponse> EnrichContext<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var headers = context.Options.Headers ?? new Metadata();
        var httpContext = httpContextAccessor.HttpContext;

        var correlationId = httpContext?.ResolveCorrelationId() ?? Guid.NewGuid().ToString("N");
        SafeAdd(headers, AppGrpcMetadataKeys.CorrelationId, correlationId);

        var traceparent = httpContext?.Request.Headers[AppHeaderNames.TraceParent].FirstOrDefault();
        if (!string.IsNullOrEmpty(traceparent))
            SafeAdd(headers, AppGrpcMetadataKeys.TraceParent, traceparent);

        var authHeader = httpContext?.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(authHeader))
            SafeAdd(headers, AppGrpcMetadataKeys.Authorization, authHeader);

        if (tenantContext.TenantId.HasValue)
            SafeAdd(headers, AppGrpcMetadataKeys.TenantId, tenantContext.TenantId.Value.ToString());
        if (tenantContext.InstitutionId.HasValue)
            SafeAdd(headers, AppGrpcMetadataKeys.InstitutionId, tenantContext.InstitutionId.Value.ToString());
        if (tenantContext.DistrictId.HasValue)
            SafeAdd(headers, AppGrpcMetadataKeys.DistrictId, tenantContext.DistrictId.Value.ToString());
        if (tenantContext.RegionId.HasValue)
            SafeAdd(headers, AppGrpcMetadataKeys.RegionId, tenantContext.RegionId.Value.ToString());

        SafeAdd(headers, AppGrpcMetadataKeys.TenantScope, tenantContext.Scope.ToString().ToLowerInvariant());

        if (currentUser.UserId.HasValue)
            SafeAdd(headers, AppGrpcMetadataKeys.UserId, currentUser.UserId.Value.ToString());

        var deadline = context.Options.Deadline ?? DateTime.UtcNow.AddSeconds(10);
        var newOptions = context.Options.WithHeaders(headers).WithDeadline(deadline);

        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, newOptions);
    }

    private static void SafeAdd(Metadata headers, string key, string value)
    {
        if (!headers.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            headers.Add(key, value);
    }
}
