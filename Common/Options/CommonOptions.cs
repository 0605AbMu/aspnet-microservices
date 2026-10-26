namespace Microservices.Common.Options;

public class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public ushort? PrefetchCount { get; set; } = 32;
    public int? ConcurrentMessageLimit { get; set; } = 16;

    /// <summary>
    /// Parallel transactional-outbox delivery loops per service instance (1 = MassTransit's default single loop).
    /// With the ReadCommitted outbox isolation a single loop already drains several hundred messages per second;
    /// measured on a dev machine, extra loops did not drain faster but competed with the request path for commits
    /// (create throughput dropped by up to 60% at 4 loops). Raise it only after measuring on your storage.
    /// </summary>
    public int OutboxDeliveryWorkers { get; set; } = 1;
}

public class IdentityOptions
{
    public string Issuer { get; set; } = "https://identity.local";
    public string Audience { get; set; } = "microservices";
    public string JwksUri { get; set; } = "http://localhost:5001/.well-known/jwks.json";
}

public class ServiceOptions
{
    public string Name { get; set; } = "unknown-service";
    public string Version { get; set; } = "1.0.0";
}

public class OpenTelemetryOptions
{
    public string Endpoint { get; set; } = "http://localhost:4317";
    public double SamplingRatio { get; set; } = 1.0;
}

public class RateLimitOptions
{
    public int AnonymousPerMinute { get; set; } = 30;
    public int AuthenticatedPerMinute { get; set; } = 300;
    public int StrictPerMinute { get; set; } = 10;
}

public class MinioOptions
{
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = "minioadmin";
    public string SecretKey { get; set; } = "minioadmin";
    public bool WithSsl { get; set; } = false;
    public string DefaultBucket { get; set; } = "microservices";
}
