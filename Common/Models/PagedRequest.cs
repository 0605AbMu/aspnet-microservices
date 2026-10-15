using ProtoBuf;
using System.ComponentModel;

namespace Microservices.Common.Models;

[ProtoContract]
public class PagedRequest : IPagedRequest
{
    [ProtoMember(1)]
    [DefaultValue(1)]
    public int Page { get; set; } = 1;

    [ProtoMember(2)]
    [DefaultValue(20)]
    public int Size { get; set; } = 20;

    [ProtoIgnore]
    public int PageSize => Size;

    [ProtoMember(3)]
    [DefaultValue("Id")]
    public string? SortBy { get; set; } = "Id";

    [ProtoMember(4)]
    [DefaultValue(true)]
    public bool SortDescending { get; set; } = true;
}

[ProtoContract]
public class PagedResult<T>
{
    [ProtoMember(1)]
    public IReadOnlyList<T> Items { get; init; } = [];

    [ProtoMember(2)]
    public int Total { get; init; }

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int total)
    {
        Items = items;
        Total = total;
    }
}
