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

    private long? _userId;
    private bool _userIdResolved;
    public long? UserId
    {
        get
        {
            if (!_userIdResolved)
            {
                var raw = User?.FindFirstValue(AppClaimTypes.Sub) ?? User?.FindFirstValue(AppClaimTypes.UserId);
                _userId = long.TryParse(raw, out var id) ? id : null;
                _userIdResolved = true;
            }
            return _userId;
        }
    }

    private string? _username;
    private bool _usernameResolved;
    public string? Username
    {
        get
        {
            if (!_usernameResolved)
            {
                _username = User?.FindFirstValue(AppClaimTypes.Username) ?? User?.FindFirstValue(ClaimTypes.Name);
                _usernameResolved = true;
            }
            return _username;
        }
    }

    private string? _sessionId;
    private bool _sessionIdResolved;
    public string? SessionId
    {
        get
        {
            if (!_sessionIdResolved)
            {
                _sessionId = User?.FindFirstValue(AppClaimTypes.SessionId);
                _sessionIdResolved = true;
            }
            return _sessionId;
        }
    }

    private string? _securityStamp;
    private bool _securityStampResolved;
    public string? SecurityStamp
    {
        get
        {
            if (!_securityStampResolved)
            {
                _securityStamp = User?.FindFirstValue(AppClaimTypes.SecurityStamp);
                _securityStampResolved = true;
            }
            return _securityStamp;
        }
    }

    private string? _clientIp;
    private bool _clientIpResolved;
    public string? ClientIp
    {
        get
        {
            if (!_clientIpResolved)
            {
                _clientIp = HttpContext?.Connection.RemoteIpAddress?.ToString()
                    ?? HttpContext?.Request.Headers[AppHeaderNames.XForwardedFor].FirstOrDefault()?.Split(',')[0].Trim();
                _clientIpResolved = true;
            }
            return _clientIp;
        }
    }

    private string? _userAgent;
    private bool _userAgentResolved;
    public string? UserAgent
    {
        get
        {
            if (!_userAgentResolved)
            {
                _userAgent = HttpContext?.Request.Headers[AppHeaderNames.UserAgent].ToString();
                _userAgentResolved = true;
            }
            return _userAgent;
        }
    }

    private string? _correlationId;
    private bool _correlationIdResolved;
    public string? CorrelationId
    {
        get
        {
            if (!_correlationIdResolved)
            {
                _correlationId = HttpContext?.ResolveCorrelationId();
                _correlationIdResolved = true;
            }
            return _correlationId;
        }
    }

    private IReadOnlyList<string>? _roles;
    public IReadOnlyList<string> Roles => _roles ??= 
        (User?.FindAll(AppClaimTypes.Role).Select(c => c.Value).ToList()
        ?? User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? []);

    private IReadOnlyList<string>? _permissions;
    public IReadOnlyList<string> Permissions => _permissions ??=
        (User?.FindAll(AppClaimTypes.Permission).Select(c => c.Value).ToList() ?? []);

    private HashSet<string>? _permissionSet;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated is true;

    public bool IsInRole(string role) => User?.IsInRole(role) is true;

    public bool HasPermission(string permission)
    {
        _permissionSet ??= new HashSet<string>(Permissions, StringComparer.Ordinal);
        return _permissionSet.Contains(permission);
    }
}
