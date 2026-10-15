using System.ComponentModel.DataAnnotations;
using Microservices.Common.Domain;

namespace Microservices.Template.Service.Domain;

public class TemplateItem : AggregateRoot<long>, ITenantEntity
{
    public long? TenantId { get; set; }
    public long? RegionId { get; set; }
    public long? DistrictId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "Active";
}
