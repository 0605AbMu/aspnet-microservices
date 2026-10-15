namespace Microservices.Common.Domain;

public class EntityAuditedEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
    public string EntityName { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty; // "Created", "Modified", "Deleted"
    public string? OldValues { get; init; }
    public string? NewValues { get; init; }
    public long? UserId { get; init; }
    public string? ClientIP { get; init; }
    public string? UserAgent { get; init; }
    public string? DigitalSignatureHash { get; init; }
}
