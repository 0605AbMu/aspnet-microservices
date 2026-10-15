using Microservices.Template.Service.Persistence;
using Microservices.Template.Service.Services;
using Microservices.WebCore.Extensions;

namespace Microservices.Template.Service.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTemplateInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMicroserviceDbContext<TemplateDbContext>(configuration);
        services.AddMicroserviceMassTransit<TemplateDbContext>(configuration);
        services.AddMicroserviceGrpcServer();

        return services;
    }

    public static IServiceCollection AddTemplateApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<TemplateItemService>();

        return services;
    }
}
