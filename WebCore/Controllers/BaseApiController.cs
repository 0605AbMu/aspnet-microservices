using Microservices.Common.Models;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Microservices.WebCore.Controllers;

/// <summary>
/// Shared base controller for HTTP APIs.
/// Automatically attaches CorrelationId to ApiResponse envelopes.
/// </summary>
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected string CorrelationId => HttpContext?.ResolveCorrelationId() ?? string.Empty;

    protected ActionResult<ApiResponse<T>> OkResponse<T>(T data, string? message = null) =>
        Ok(ApiResponse<T>.Ok(data, message, correlationId: CorrelationId));

    protected ActionResult<ApiResponse<T>> CreatedResponse<T>(string uri, T data, string? message = null) =>
        Created(uri, ApiResponse<T>.Ok(data, message, code: 201, correlationId: CorrelationId));

    protected ActionResult<ApiResponse<T>> FailResponse<T>(string message, int code = 400, T? data = default) =>
        StatusCode(code, ApiResponse<T>.Fail(message, code, data, correlationId: CorrelationId));
}
