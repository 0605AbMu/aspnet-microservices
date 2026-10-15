using MassTransit;
using Microservices.Common.Context;
using Microservices.Common.Filtering;
using Microservices.Common.Models;
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
    public async Task<PagedResult<TemplateItemDto>> GetPagedAsync(
        PagedRequest request,
        IDictionary<string, FilterRule>? filters,
        CancellationToken ct = default)
    {
        var pagedEntities = await dbContext.TemplateItems
            .AsNoTracking()
            .ToPagedResultAsync(request, filters, ct);

        var dtos = pagedEntities.Items.Select(MapToDto).ToList();
        return new PagedResult<TemplateItemDto>(dtos, pagedEntities.Total);
    }

    public async Task<TemplateItemDto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var item = await dbContext.TemplateItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new TemplateItemNotFoundException(id);

        return MapToDto(item);
    }

    public async Task<TemplateItemDto> CreateAsync(CreateTemplateItemRequest request, CancellationToken ct = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var item = new TemplateItem
        {
            TenantId = tenantContext.TenantId,
            Title = request.Title,
            Description = request.Description,
            Status = "Active"
        };

        dbContext.TemplateItems.Add(item);
        await dbContext.SaveChangesAsync(ct);

        // Publish to transactional outbox with the generated database Id within the active transaction
        await publishEndpoint.Publish(new TemplateItemCreatedIntegrationEvent
        {
            ItemId = item.Id,
            Title = item.Title
        }, ct);

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

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

    private static TemplateItemDto MapToDto(TemplateItem entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Description = entity.Description,
        Status = entity.Status,
        CreatedAt = entity.CreatedAt
    };
}
