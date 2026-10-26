using System.Linq.Expressions;
using MassTransit;
using Microservices.Common.Context;
using Microservices.Common.Filtering;
using Microservices.Common.Models;
using Microservices.Common.Persistence.Extensions;
using Microservices.Template.Contracts.Dtos;
using Microservices.Template.Contracts.Events;
using Microservices.Template.Contracts.Exceptions;
using Microservices.Template.Service.Domain;
using Microservices.Template.Service.Persistence;
using Microservices.WebCore.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Microservices.Template.Service.Services;

public class TemplateItemService(
    TemplateDbContext dbContext,
    IPublishEndpoint publishEndpoint,
    ITenantContext tenantContext,
    ILogger<TemplateItemService> logger)
{
    // Single source of truth for the entity -> DTO mapping: translated to SQL for reads (only the 5 columns the
    // DTO needs are fetched, not the whole 13-column row) and compiled once for the write paths.
    private static readonly Expression<Func<TemplateItem, TemplateItemDto>> ToDto = x => new TemplateItemDto
    {
        Id = x.Id,
        Title = x.Title,
        Description = x.Description,
        Status = x.Status,
        CreatedAt = x.CreatedAt
    };

    private static readonly Func<TemplateItem, TemplateItemDto> MapToDto = ToDto.Compile();

    public Task<PagedResult<TemplateItemDto>> GetPagedAsync(
        PagedRequest request,
        IDictionary<string, FilterRule>? filters,
        CancellationToken ct = default) =>
        dbContext.TemplateItems.ToPagedResultAsync(request, ToDto, filters, ct);

    public async Task<TemplateItemDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await dbContext.TemplateItems
            .Where(x => x.Id == id)
            .Select(ToDto)
            .FirstOrDefaultAsync(ct)
            ?? throw new TemplateItemNotFoundException(id);
    }

    public async Task<TemplateItemDto> CreateAsync(CreateTemplateItemRequest request, CancellationToken ct = default)
    {
        var item = new TemplateItem
        {
            TenantId = tenantContext.TenantId,
            Title = request.Title,
            Description = request.Description,
            Status = "Active"
        };

        // Id comes from the HiLo sequence block: it is known here, without an INSERT round trip.
        await dbContext.TemplateItems.AddAsync(item, ct);

        await publishEndpoint.Publish(new TemplateItemCreatedIntegrationEvent
        {
            ItemId = item.Id,
            Title = item.Title
        }, ct);

        // One SaveChanges = one batch, one transaction: the row and its outbox message commit together.
        await dbContext.SaveChangesAsync(ct);

        logger.LogInfo("TemplateItem with Id {0} created successfully", item.Id);

        return MapToDto(item);
    }

    public async Task<TemplateItemDto> UpdateAsync(long id, UpdateTemplateItemRequest request, CancellationToken ct = default)
    {
        var item = await dbContext.TemplateItems
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new TemplateItemNotFoundException(id);

        var oldStatus = item.Status;
        item.Title = request.Title;
        item.Description = request.Description;
        item.Status = request.Status;

        if (oldStatus != request.Status)
        {
            await publishEndpoint.Publish(new TemplateItemStatusChangedIntegrationEvent
            {
                ItemId = item.Id,
                OldStatus = oldStatus,
                NewStatus = request.Status
            }, ct);
        }

        await dbContext.SaveChangesAsync(ct);

        return MapToDto(item);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var item = await dbContext.TemplateItems
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new TemplateItemNotFoundException(id);

        dbContext.TemplateItems.Remove(item);
        await dbContext.SaveChangesAsync(ct);
    }

}
