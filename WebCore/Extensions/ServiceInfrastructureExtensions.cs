using MassTransit;
using Microservices.Common.Constants;
using Microservices.Common.Options;
using Microservices.Common.Persistence.Interceptors;
using Microservices.WebCore.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf.Grpc.ClientFactory;
using ProtoBuf.Grpc.Server;

namespace Microservices.WebCore.Extensions;

/// <summary>
/// Shared DI registration extensions for PostgreSQL DbContext pooling, MassTransit + Outbox,
/// and Code-First gRPC servers/clients across all microservices.
/// </summary>
public static class ServiceInfrastructureExtensions
{
    /// <summary>
    /// Registers a scoped PostgreSQL DbContext with retry-on-failure, snake_case naming convention,
    /// and the asynchronous AuditSaveChangesInterceptor.
    /// Uses scoped DbContext lifetime to guarantee safe multi-tenant context injection without pool leakage.
    /// </summary>
    public static IServiceCollection AddMicroserviceDbContext<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        int commandTimeoutSeconds = 10,
        string connectionName = ConnectionNames.Default)
        where TDbContext : DbContext
    {
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");

        services.AddDbContext<TDbContext>((sp, options) =>
        {
            var auditInterceptor = sp.GetRequiredService<AuditSaveChangesInterceptor>();

            options
                .UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                    npgsql.CommandTimeout(commandTimeoutSeconds);
                    npgsql.MigrationsAssembly(typeof(TDbContext).Assembly.FullName);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(auditInterceptor);
        });

        return services;
    }

    [Obsolete("Use AddMicroserviceDbContext instead for safe scoped multi-tenant DbContext lifecycle.")]
    public static IServiceCollection AddMicroserviceDbContextPool<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        int commandTimeoutSeconds = 10,
        string connectionName = ConnectionNames.Default)
        where TDbContext : DbContext =>
        services.AddMicroserviceDbContext<TDbContext>(configuration, commandTimeoutSeconds, connectionName);

    /// <summary>
    /// Registers MassTransit with RabbitMQ, PostgreSQL EF Core Transactional Outbox,
    /// and standard exponential message retry.
    /// </summary>
    public static IServiceCollection AddMicroserviceMassTransit<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureBus = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureRabbitMq = null)
        where TDbContext : DbContext
    {
        services.Configure<RabbitMqOptions>(configuration.GetSection(ConfigSections.RabbitMQ));
        var rabbitOptions = configuration.GetSection(ConfigSections.RabbitMQ).Get<RabbitMqOptions>() ?? new();

        services.AddMassTransit(x =>
        {
            configureBus?.Invoke(x);

            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitOptions.Host, rabbitOptions.Port, "/", h =>
                {
                    h.Username(rabbitOptions.Username);
                    h.Password(rabbitOptions.Password);
                });

                configureRabbitMq?.Invoke(context, cfg);

                cfg.UseMessageRetry(r => r.Exponential(
                    3,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(2)));

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    /// <summary>
    /// Registers Code-First gRPC server with context &amp; exception interceptors.
    /// </summary>
    public static IServiceCollection AddMicroserviceGrpcServer(this IServiceCollection services)
    {
        services.AddCodeFirstGrpc(options =>
        {
            options.Interceptors.Add<GrpcServerContextInterceptor>();
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });

        return services;
    }

    /// <summary>
    /// Registers a Code-First gRPC client with automatic context &amp; deadline propagation.
    /// </summary>
    public static IHttpClientBuilder AddMicroserviceGrpcClient<TClient>(
        this IServiceCollection services,
        string address)
        where TClient : class
    {
        return services
            .AddCodeFirstGrpcClient<TClient>(o => o.Address = new Uri(address))
            .AddInterceptor<GrpcClientContextInterceptor>();
    }
}
