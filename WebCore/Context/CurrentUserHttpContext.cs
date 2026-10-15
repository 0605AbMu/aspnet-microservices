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
            var raw = User?.FindFirstValue(AppClaimTypes.Sub) ?? User?.FindFirstValue(AppClaimTypes.UserId);
            return long.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Username =>
        User?.FindFirstValue(AppClaimTypes.Username) ?? User?.FindFirstValue(ClaimTypes.Name);

    public string? SessionId => User?.FindFirstValue(AppClaimTypes.SessionId);

    public string? SecurityStamp => User?.FindFirstValue(AppClaimTypes.SecurityStamp);

    public string? ClientIp
    {
        get
        {
            var header = HttpContext?.Request.Headers[AppHeaderNames.XForwardedFor].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(header))
            {
                return header.Split(',')[0].Trim();
            }
            return HttpContext?.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent => HttpContext?.Request.Headers[AppHeaderNames.UserAgent].ToString();

    public string? CorrelationId => HttpContext?.ResolveCorrelationId();

    public IReadOnlyList<string> Roles =>
        User?.FindAll(AppClaimTypes.Role).Select(c => c.Value).ToList()
        ?? User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public IReadOnlyList<string> Permissions =>
        User?.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToList() ?? [];

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated is true;

    public bool IsInRole(string role) => User?.IsInRole(role) is true;

    public bool HasPermission(string permission) =>
        User?.FindAll(AppClaimTypes.Permission).Any(c => c.Value == permission) is true;
}
