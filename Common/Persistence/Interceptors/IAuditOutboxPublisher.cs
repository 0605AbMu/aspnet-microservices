using Microservices.Common.Domain;

namespace Microservices.Common.Persistence.Interceptors;

public interface IAuditOutboxPublisher
{
    Task PublishAuditAsync(EntityAuditedEvent auditEvent, CancellationToken cancellationToken = default);
}
