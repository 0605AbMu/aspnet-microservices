using ProtoBuf;

namespace Microservices.Template.Contracts.Dtos;

[ProtoContract]
public class TemplateItemDto
{
    [ProtoMember(1)]
    public long Id { get; init; }

    [ProtoMember(2)]
    public string Title { get; init; } = string.Empty;

    [ProtoMember(3)]
    public string? Description { get; init; }

    [ProtoMember(4)]
    [System.ComponentModel.DefaultValue("Active")]
    public string Status { get; init; } = "Active";

    [ProtoMember(5)]
    public DateTimeOffset CreatedAt { get; init; }
}

[ProtoContract]
public class CreateTemplateItemRequest
{
    [ProtoMember(1)]
    public string Title { get; init; } = string.Empty;

    [ProtoMember(2)]
    public string? Description { get; init; }
}

[ProtoContract]
public class UpdateTemplateItemRequest
{
    [ProtoMember(1)]
    public string Title { get; init; } = string.Empty;

    [ProtoMember(2)]
    public string? Description { get; init; }

    [ProtoMember(3)]
    [System.ComponentModel.DefaultValue("Active")]
    public string Status { get; init; } = "Active";
}

[ProtoContract]
public class GetTemplateItemByIdRequest
{
    [ProtoMember(1)]
    public long Id { get; init; }
}
