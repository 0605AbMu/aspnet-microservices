using System.ServiceModel;
using Microservices.Template.Contracts.Dtos;
using ProtoBuf.Grpc;

namespace Microservices.Template.Contracts.Services;

[ServiceContract]
public interface ITemplateGrpcService
{
    [OperationContract]
    Task<TemplateItemDto> GetByIdAsync(GetTemplateItemByIdRequest request, CallContext context = default);
}
