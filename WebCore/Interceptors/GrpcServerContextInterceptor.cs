using Grpc.Core;
using Grpc.Core.Interceptors;
using Microservices.Common.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Interceptors;

/// <summary>
/// Incoming gRPC server interceptor.
/// Reads x-correlation-id and traceparent from gRPC metadata and populates HttpContext.Items.
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

        var correlationId = context.RequestHeaders
            .FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.CorrelationId, StringComparison.OrdinalIgnoreCase))?.Value
            ?? Guid.NewGuid().ToString("N");

        httpContext.Items[AppItemKeys.CorrelationId] = correlationId;

        var traceparent = context.RequestHeaders
            .FirstOrDefault(e => e.Key.Equals(AppGrpcMetadataKeys.TraceParent, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrEmpty(traceparent))
            httpContext.Items[AppItemKeys.TraceParent] = traceparent;

        logger.LogDebug("gRPC incoming call. Method={Method} CorrelationId={CorrelationId}",
            context.Method, correlationId);
    }
}
