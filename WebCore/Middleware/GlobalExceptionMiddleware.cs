using System.Net;
using Microservices.Common.Exceptions;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.ResolveCorrelationId();
        var (httpStatus, message, code, args, logDetails) = MapException(exception);

        if (httpStatus == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception,
                "Unhandled exception. CorrelationId={CorrelationId} Path={Path} LogDetails={LogDetails}",
                correlationId, context.Request.Path, logDetails);
        }
        else
        {
            logger.LogWarning(
                "Handled [{ExType}] {Message}. CorrelationId={CorrelationId} LogDetails={LogDetails}",
                exception.GetType().Name, exception.Message, correlationId, logDetails);
        }

        await context.WriteErrorResponseAsync(code, message, args);
    }

    private static (HttpStatusCode status, string message, int code, object? data, string? logDetails) MapException(Exception exception)
    {
        if (exception is AppException appEx)
        {
            var status = appEx.ErrorType switch
            {
                ErrorType.NotFound => HttpStatusCode.NotFound,
                ErrorType.Validation => HttpStatusCode.BadRequest,
                ErrorType.Conflict => HttpStatusCode.Conflict,
                ErrorType.Forbidden => HttpStatusCode.Forbidden,
                ErrorType.Unauthorized => HttpStatusCode.Unauthorized,
                _ => HttpStatusCode.InternalServerError
            };

            var code = (int)status;
            return (status, appEx.Message, code, appEx.Args, appEx.LogDetails);
        }

        if (exception is OperationCanceledException)
        {
            return (HttpStatusCode.RequestTimeout, "request_timeout", 408, null, null);
        }

        return (HttpStatusCode.InternalServerError, "server_error", 500, null, null);
    }
}
