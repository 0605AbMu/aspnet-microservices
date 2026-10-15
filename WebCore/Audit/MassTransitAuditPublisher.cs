using MassTransit;
using Microservices.Common.Domain;
using Microservices.Common.Persistence.Interceptors;
using Microsoft.Extensions.Logging;

namespace Microservices.WebCore.Audit;

/// <summary>
/// Publishes EntityAuditedEvent to MassTransit Transactional Outbox.
/// Ensures non-repudiation with zero latency impact on the main database transaction.
/// </summary>
public class MassTransitAuditPublisher(
    IPublishEndpoint publishEndpoint,
    ILogger<MassTransitAuditPublisher> logger) : IAuditOutboxPublisher
{
    public async Task PublishAuditAsync(EntityAuditedEvent auditEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            await publishEndpoint.Publish(auditEvent, cancellationToken);
            logger.LogDebug("Published audit event for {EntityName} #{EntityId} ({Action})",
                auditEvent.EntityName, auditEvent.EntityId, auditEvent.Action);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to publish audit event for {EntityName} #{EntityId}",
                auditEvent.EntityName, auditEvent.EntityId);
        }
    }
}
