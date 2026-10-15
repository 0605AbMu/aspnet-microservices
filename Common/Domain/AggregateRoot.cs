using System.ComponentModel.DataAnnotations;

namespace Microservices.Common.Domain;

public abstract class AggregateRoot<TId> : AuditableEntity<TId>
{
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
