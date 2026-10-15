namespace Microservices.Common.Context;

public interface ICurrentUser
{
    long? UserId { get; }
    string? Username { get; }
    string? SessionId { get; }
    string? SecurityStamp { get; }
    string? ClientIp { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
}

public class SystemCurrentUser : ICurrentUser
{
    public static readonly SystemCurrentUser Instance = new();

    public long? UserId => 0;
    public string? Username => "system";
    public string? SessionId => "system-session";
    public string? SecurityStamp => "system";
    public string? ClientIp => "127.0.0.1";
    public string? UserAgent => "BackgroundService";
    public string? CorrelationId => Guid.NewGuid().ToString("N");
    public IReadOnlyList<string> Roles => ["System", "Administrator"];
    public IReadOnlyList<string> Permissions => ["*"];
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => true;
    public bool HasPermission(string permission) => true;
}
