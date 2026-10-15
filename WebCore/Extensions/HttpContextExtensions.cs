using System.Text.Json;
using System.Text.Json.Serialization;
using Microservices.Common.Constants;
using Microservices.Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Microservices.WebCore.Extensions;

public static class HttpContextExtensions
{
    public static readonly JsonSerializerOptions SharedJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Resolves CorrelationId from HttpContext.Items or X-Correlation-Id request header,
    /// generating and caching a new GUID in HttpContext.Items if none exists yet.
    /// </summary>
    public static string ResolveCorrelationId(this HttpContext context)
    {
        if (context.Items.TryGetValue(AppItemKeys.CorrelationId, out var existing) &&
            existing is string existingStr &&
            !string.IsNullOrWhiteSpace(existingStr))
        {
            return existingStr;
        }

        var correlationId = context.Request.Headers[AppHeaderNames.CorrelationId].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Items[AppItemKeys.CorrelationId] = correlationId;
        return correlationId;
    }

    /// <summary>
    /// Resolves controller action name or route display name for structured logging.
    /// </summary>
    public static string ResolveEndpointMethodName(this HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var actionDescriptor = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return actionDescriptor?.ActionName
            ?? endpoint?.DisplayName
            ?? $"{context.Request.Method} {context.Request.Path}";
    }

    /// <summary>
    /// Resolves controller class name or endpoint group for structured logging.
    /// </summary>
    public static string ResolveEndpointClassName(this HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var actionDescriptor = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return actionDescriptor?.ControllerTypeInfo.Name ?? "HttpEndpoint";
    }

    /// <summary>
    /// Writes a standardized camelCase ApiResponse JSON error payload to the HTTP response.
    /// </summary>
    public static Task WriteErrorResponseAsync(
        this HttpContext context,
        int statusCode,
        string message,
        object? data = null,
        CancellationToken cancellationToken = default)
    {
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var correlationId = context.ResolveCorrelationId();
        var responseEnvelope = ApiResponse<object?>.Fail(message, statusCode, data, correlationId);

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            responseEnvelope,
            SharedJsonOptions,
            cancellationToken);
    }
}
