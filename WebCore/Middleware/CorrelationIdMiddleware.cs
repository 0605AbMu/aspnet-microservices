using Microservices.Common.Constants;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Http;

namespace Microservices.WebCore.Middleware;

/// <summary>
/// Injects X-Correlation-Id into HttpContext.Items for each request.
/// Reads from incoming header or generates a new GUID.
/// Appends the same ID to outgoing response headers.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.ResolveCorrelationId();
        context.Request.Headers[AppHeaderNames.CorrelationId] = correlationId;
        context.Response.Headers[AppHeaderNames.CorrelationId] = correlationId;

        await next(context);
    }
}
