using Grpc.Core;
using Grpc.Core.Interceptors;
using Microservices.Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Interceptors;

/// <summary>
/// Server-side gRPC interceptor that catches AppException and translates ErrorType
/// into corresponding Grpc.Core.StatusCode.
/// </summary>
public class GrpcExceptionInterceptor(ILogger<GrpcExceptionInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (AppException ex)
        {
            throw MapToRpcException(ex, context);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            logger.LogError(ex, "Unhandled exception in gRPC method {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (AppException ex)
        {
            throw MapToRpcException(ex, context);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            logger.LogError(ex, "Unhandled exception in streaming gRPC method {Method}", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    private RpcException MapToRpcException(AppException ex, ServerCallContext context)
    {
        var statusCode = ex.ErrorType switch
        {
            ErrorType.NotFound => StatusCode.NotFound,
            ErrorType.Conflict => StatusCode.AlreadyExists,
            ErrorType.Forbidden => StatusCode.PermissionDenied,
            ErrorType.Unauthorized => StatusCode.Unauthenticated,
            ErrorType.Validation => StatusCode.InvalidArgument,
            _ => StatusCode.Internal
        };

        logger.LogWarning("gRPC [{StatusCode}] in {Method}: {Message}",
            statusCode, context.Method, ex.Message);

        var trailers = new Metadata
        {
            { "error_type", ex.ErrorType.ToString().ToLowerInvariant() },
            { "error_message", ex.Message }
        };

        return new RpcException(new Status(statusCode, ex.Message), trailers);
    }
}
