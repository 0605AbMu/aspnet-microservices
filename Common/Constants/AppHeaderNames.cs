namespace Microservices.Common.Constants;

public static class AppHeaderNames
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string IdempotencyKey = "Idempotency-Key";
    public const string TraceParent = "traceparent";
    public const string TenantId = "X-Tenant-Id";
    public const string UserId = "X-User-Id";
    public const string TenantScope = "X-Tenant-Scope";
    public const string Authorization = "Authorization";
    public const string UserAgent = "User-Agent";
    public const string XForwardedFor = "X-Forwarded-For";
    public const string XCache = "X-Cache";
}

public static class AppGrpcMetadataKeys
{
    public const string CorrelationId = "x-correlation-id";
    public const string TraceParent = "traceparent";
    public const string Authorization = "authorization";
    public const string TenantId = "x-tenant-id";
    public const string TenantScope = "x-tenant-scope";
    public const string UserId = "x-user-id";
}

public static class AppItemKeys
{
    public const string CorrelationId = "CorrelationId";
    public const string TraceParent = "traceparent";
    public const string UserId = "UserId";
    public const string TenantId = "TenantId";
}

public static class AppCacheKeys
{
    public const string SessionRevokedL1Format = "session_revoked:{0}:{1}"; // 0: userId, 1: sessionId/securityStamp
    public const string SessionRevokedL2Format = "revoked:session:{0}:{1}"; // 0: userId, 1: sessionId/securityStamp
}
