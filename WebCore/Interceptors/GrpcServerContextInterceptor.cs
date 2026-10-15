using System.Security.Claims;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microservices.Common.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Interceptors;

/// <summary>
/// Incoming gRPC server interceptor.
/// Reads x-correlation-id, traceparent, tenant, and user identity from gRPC metadata and populates HttpContext and User claims.
/// Pushes MethodName into Serilog LogContext.
/// </summary>
public class GrpcServerContextInterceptor(
    IHttpContextAccessor httpContextAccessor,
    ILogger<GrpcServerContextInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        EnrichHttpContext(context);
        using (Serilog.Context.LogContext.PushProperty("MethodName", context.Method))
        {
            return await continuation(request, context);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        EnrichHttpContext(context);
        using (Serilog.Context.LogContext.PushProperty("MethodName", context.Method))
        {
            await continuation(request, responseStream, context);
        }
    }

    private void EnrichHttpContext(ServerCallContext context)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null) return;

        var headers = context.RequestHeaders;

        var correlationId = headers
            .FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.CorrelationId, StringComparison.OrdinalIgnoreCase))?.Value
            ?? Guid.NewGuid().ToString("N");

        httpContext.Items[AppItemKeys.CorrelationId] = correlationId;

        var traceparent = headers
            .FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.TraceParent, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(traceparent))
            httpContext.Items[AppItemKeys.TraceParent] = traceparent;

        var claims = new List<Claim>();

        var tenantId = headers.FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.TenantId, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(tenantId))
        {
            httpContext.Items[AppItemKeys.TenantId] = tenantId;
            claims.Add(new Claim(AppClaimTypes.TenantId, tenantId));
        }

        var districtId = headers.FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.DistrictId, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(districtId))
            claims.Add(new Claim(AppClaimTypes.DistrictId, districtId));

        var regionId = headers.FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.RegionId, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(regionId))
            claims.Add(new Claim(AppClaimTypes.RegionId, regionId));

        var tenantScope = headers.FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.TenantScope, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(tenantScope))
            claims.Add(new Claim(AppClaimTypes.TenantScope, tenantScope));

        var userId = headers.FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.UserId, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            httpContext.Items[AppItemKeys.UserId] = userId;
            claims.Add(new Claim(AppClaimTypes.Sub, userId));
            claims.Add(new Claim(AppClaimTypes.UserId, userId));
        }

        var authHeader = headers.FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.Authorization, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(authHeader) && !httpContext.Request.Headers.ContainsKey(AppHeaderNames.Authorization))
        {
            httpContext.Request.Headers[AppHeaderNames.Authorization] = authHeader;
        }

        if (claims.Count > 0)
        {
            var identity = new ClaimsIdentity(claims, "gRPC");
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                httpContext.User.AddIdentity(identity);
            }
            else
            {
                httpContext.User = new ClaimsPrincipal(identity);
            }
        }

        logger.LogDebug("gRPC incoming call. Method={Method} CorrelationId={CorrelationId} TenantId={TenantId} UserId={UserId}",
            context.Method, correlationId, tenantId ?? "none", userId ?? "none");
    }
}
