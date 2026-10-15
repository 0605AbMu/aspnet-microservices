using Microservices.Common.Filtering;
using Microservices.Common.Models;
using Microservices.Template.Contracts.Dtos;
using Microservices.Template.Service.Services;
using Microservices.WebCore.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Microservices.Template.Service.Controllers;

[Route("api/v1/template-items")]
public class TemplateItemsController(TemplateItemService itemService) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<TemplateItemDto>>>> GetList(
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        var result = await itemService.GetPagedAsync(request, null, ct);
        return OkResponse(result);
    }

    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<PagedResult<TemplateItemDto>>>> Search(
        [FromQuery] PagedRequest request,
        [FromBody] Dictionary<string, FilterRule>? filters,
        CancellationToken ct)
    {
        var result = await itemService.GetPagedAsync(request, filters, ct);
        return OkResponse(result);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<TemplateItemDto>>> GetById(long id, CancellationToken ct)
    {
        var item = await itemService.GetByIdAsync(id, ct);
        return OkResponse(item);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TemplateItemDto>>> Create(
        [FromBody] CreateTemplateItemRequest request,
        CancellationToken ct)
    {
        var created = await itemService.CreateAsync(request, ct);
        return CreatedResponse($"/api/v1/template-items/{created.Id}", created);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ApiResponse<TemplateItemDto>>> Update(
        long id,
        [FromBody] UpdateTemplateItemRequest request,
        CancellationToken ct)
    {
        var updated = await itemService.UpdateAsync(id, request, ct);
        return OkResponse(updated);
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(long id, CancellationToken ct)
    {
        await itemService.DeleteAsync(id, ct);
        return OkResponse(true, "template_item_deleted");
    }
}
