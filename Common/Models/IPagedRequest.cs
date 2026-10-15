namespace Microservices.Common.Models;

public interface IPagedRequest
{
    int Page { get; }
    int PageSize { get; }
}
