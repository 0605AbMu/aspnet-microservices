using Microservices.Template.Contracts.Dtos;
using Microservices.Template.Contracts.Services;
using Microservices.Template.Service.Services;
using ProtoBuf.Grpc;

namespace Microservices.Template.Service.Grpc;

public class TemplateGrpcService(TemplateItemService itemService) : ITemplateGrpcService
{
    public async Task<TemplateItemDto> GetByIdAsync(GetTemplateItemByIdRequest request, CallContext context = default)
    {
        return await itemService.GetByIdAsync(request.Id, context.CancellationToken);
    }
}
