namespace Microservices.Common.Constants;

public static class ConnectionNames
{
    public const string Default = "Default";
    public const string ReadOnly = "ReadOnly";
    public const string Redis = "Redis";
    public const string RabbitMQ = "RabbitMQ";
    public const string Hangfire = "Hangfire";
}

public static class ConfigSections
{
    public const string ConnectionStrings = "ConnectionStrings";
    public const string RabbitMQ = "RabbitMQ";
    public const string Identity = "Identity";
    public const string Service = "Service";
    public const string OpenTelemetry = "OpenTelemetry";
    public const string Redis = "Redis";
    public const string RateLimiting = "RateLimiting";
    public const string Minio = "Minio";
}
