namespace Microservices.Common.Options;

public class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public ushort Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
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
