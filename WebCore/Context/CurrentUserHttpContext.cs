using System.Security.Claims;
using Microservices.Common.Constants;
using Microservices.Common.Context;
using Microservices.WebCore.Extensions;
using Microsoft.AspNetCore.Http;

namespace Microservices.WebCore.Context;

/// <summary>
/// Reads current user identity and client context (IP, UserAgent, CorrelationId) from HttpContext.
/// Registered as Scoped — per-request lifetime.
/// </summary>
public class CurrentUserHttpContext(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private HttpContext? HttpContext => httpContextAccessor.HttpContext;
    private ClaimsPrincipal? User => HttpContext?.User;

    public long? UserId
    {
        get
        {
            if (field is null && User?.Identity?.IsAuthenticated is true)
            {
                var raw = User.FindFirstValue(AppClaimTypes.Sub) ?? User.FindFirstValue(AppClaimTypes.UserId);
                field = long.TryParse(raw, out var id) ? id : null;
            }
            return field;
        }
    }

    public string? Username
    {
        get
        {
            if (field is null && User?.Identity?.IsAuthenticated is true)
            {
                field = User.FindFirstValue(AppClaimTypes.Username) ?? User.FindFirstValue(ClaimTypes.Name);
            }
            return field;
        }
    }

    public string? SessionId
    {
        get
        {
            if (field is null && User?.Identity?.IsAuthenticated is true)
            {
                field = User.FindFirstValue(AppClaimTypes.SessionId);
            }
            return field;
        }
    }

    public string? SecurityStamp
    {
        get
        {
            if (field is null && User?.Identity?.IsAuthenticated is true)
            {
                field = User.FindFirstValue(AppClaimTypes.SecurityStamp);
            }
            return field;
        }
    }

    public string? ClientIp
    {
        get => field ??= HttpContext?.Connection.RemoteIpAddress?.ToString()
            ?? HttpContext?.Request.Headers[AppHeaderNames.XForwardedFor].FirstOrDefault()?.Split(',')[0].Trim();
    }

    public string? UserAgent
    {
        get => field ??= HttpContext?.Request.Headers[AppHeaderNames.UserAgent].ToString();
    }

    public string? CorrelationId
    {
        get => field ??= HttpContext?.ResolveCorrelationId();
    }

    public IReadOnlyList<string> Roles
    {
        get => field ??= (User?.FindAll(AppClaimTypes.Role).Select(c => c.Value).ToList()
            ?? User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? []);
    }

    public IReadOnlyList<string> Permissions
    {
        get => field ??= (User?.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToList() ?? []);
    }

    private HashSet<string>? _permissionSet;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated is true;

    public bool IsInRole(string role) => User?.IsInRole(role) is true;

    public bool HasPermission(string permission)
    {
        _permissionSet ??= new HashSet<string>(Permissions, StringComparer.Ordinal);
        return _permissionSet.Contains(permission);
    }
}
