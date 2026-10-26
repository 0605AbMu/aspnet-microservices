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

    /// <summary>
    /// When false the server skips the COUNT(*) query (cost grows with the size of the filtered set) and returns
    /// Total = -1 together with HasMore. Use it for infinite-scroll style lists on large tables.
    /// </summary>
    [ProtoMember(5)]
    [DefaultValue(true)]
    public bool IncludeTotal { get; set; } = true;
}

[ProtoContract]
public class PagedResult<T>
{
    [ProtoMember(1)]
    public IReadOnlyList<T> Items { get; init; } = [];

    [ProtoMember(2)]
    public int Total { get; init; }

    /// <summary>True when at least one more page exists after this one.</summary>
    [ProtoMember(3)]
    public bool HasMore { get; init; }

    /// <summary>Value of Total when the caller opted out of counting (PagedRequest.IncludeTotal = false).</summary>
    public const int UnknownTotal = -1;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int total)
    {
        Items = items;
        Total = total;
    }
}
