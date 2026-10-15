namespace Microservices.Common.Domain;

public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }
    long? CreatedBy { get; set; }
    DateTimeOffset? UpdatedAt { get; set; }
    long? UpdatedBy { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTimeOffset? DeletedAt { get; set; }
}

public interface ITenantEntity
{
    long? TenantId { get; set; }
}
