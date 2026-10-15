namespace Microservices.Common.Domain;

public interface IDomainEvent
{
    DateTimeOffset OccurredOn { get; }
}

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
