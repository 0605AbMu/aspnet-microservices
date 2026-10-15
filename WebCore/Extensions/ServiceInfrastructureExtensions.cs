using MassTransit;
using Microservices.Common.Constants;
using Microservices.Common.Options;
using Microservices.Common.Persistence.Interceptors;
using Microservices.WebCore.Context;
using Microservices.WebCore.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// Also registers an automatic database readiness health check.
    /// </summary>
    private static Npgsql.NpgsqlDataSourceBuilder CreateTunedNpgsqlDataSourceBuilder(string connectionString)
    {
        var builder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);

        if (builder.ConnectionStringBuilder.MinPoolSize == 0)
        {
            builder.ConnectionStringBuilder.MinPoolSize = 10;
        }

        if (builder.ConnectionStringBuilder.KeepAlive == 0)
        {
            builder.ConnectionStringBuilder.KeepAlive = 30;
        }

        return builder;
    }

    /// <summary>
    /// Registers a tuned, singleton NpgsqlDataSource for statement auto-preparation,
    /// dynamic multiplexing, and optimal connection pooling.
    /// </summary>
    public static IServiceCollection AddMicroserviceNpgsqlDataSource(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionName = ConnectionNames.Default,
        Action<Npgsql.NpgsqlDataSourceBuilder>? configureBuilder = null)
    {
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");

        services.AddSingleton(sp =>
        {
            var builder = CreateTunedNpgsqlDataSourceBuilder(connectionString);
            configureBuilder?.Invoke(builder);
            return builder.Build();
        });

        return services;
    }

    public static IServiceCollection AddMicroserviceDbContext<TDbContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        int commandTimeoutSeconds = 10,
        string connectionName = ConnectionNames.Default)
        where TDbContext : DbContext
    {
        var connectionString = configuration.GetConnectionString(connectionName)
            ?? throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");

        services.AddHealthChecks().AddDbContextCheck<TDbContext>(
            name: "database",
            tags: ["ready"],
            customTestQuery: async (db, ct) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));
                return await db.Database.CanConnectAsync(cts.Token);
            });

        services.TryAddSingleton(sp =>
        {
            var builder = CreateTunedNpgsqlDataSourceBuilder(connectionString);
            return builder.Build();
        });

        services.AddDbContext<TDbContext>((sp, options) =>
        {
            var auditInterceptor = sp.GetRequiredService<AuditSaveChangesInterceptor>();
            var dataSource = sp.GetRequiredService<Npgsql.NpgsqlDataSource>();

            options
                .UseNpgsql(dataSource, npgsql =>
                {
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                    npgsql.CommandTimeout(commandTimeoutSeconds);
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
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
            x.SetKebabCaseEndpointNameFormatter();

            configureBus?.Invoke(x);

            x.AddEntityFrameworkOutbox<TDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitOptions.Host, rabbitOptions.Port, "/", h =>
                {
                    h.Username(rabbitOptions.Username);
                    h.Password(rabbitOptions.Password);
                });

                if (rabbitOptions.PrefetchCount.HasValue)
                {
                    cfg.PrefetchCount = rabbitOptions.PrefetchCount.Value;
                }
                if (rabbitOptions.ConcurrentMessageLimit.HasValue)
                {
                    cfg.ConcurrentMessageLimit = rabbitOptions.ConcurrentMessageLimit.Value;
                }

                // Attach automatic multi-tenancy context propagation filters
                cfg.UsePublishFilter(typeof(MassTransitTenantPublishFilter<>), context);
                cfg.UseSendFilter(typeof(MassTransitTenantSendFilter<>), context);
                cfg.UseConsumeFilter(typeof(MassTransitTenantConsumeFilter<>), context);

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
    /// Registers Code-First gRPC server with context &amp; exception interceptors,
    /// 16MB max payload size, and response compression support.
    /// </summary>
    public static IServiceCollection AddMicroserviceGrpcServer(
        this IServiceCollection services,
        int maxMessageSizeInBytes = 16 * 1024 * 1024)
    {
        services.AddCodeFirstGrpc(options =>
        {
            options.MaxReceiveMessageSize = maxMessageSizeInBytes;
            options.MaxSendMessageSize = maxMessageSizeInBytes;
            options.ResponseCompressionLevel = System.IO.Compression.CompressionLevel.Fastest;
            options.Interceptors.Add<GrpcServerContextInterceptor>();
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });

        return services;
    }

    /// <summary>
    /// Registers a Code-First gRPC client with automatic context &amp; deadline propagation,
    /// 16MB max payload size, and tuned SocketsHttpHandler for HTTP/2 multi-stream connection pooling.
    /// </summary>
    public static IHttpClientBuilder AddMicroserviceGrpcClient<TClient>(
        this IServiceCollection services,
        string address,
        int maxMessageSizeInBytes = 16 * 1024 * 1024)
        where TClient : class
    {
        return services
            .AddCodeFirstGrpcClient<TClient>(o =>
            {
                o.Address = new Uri(address);
                o.ChannelOptionsActions.Add(opt =>
                {
                    opt.MaxReceiveMessageSize = maxMessageSizeInBytes;
                    opt.MaxSendMessageSize = maxMessageSizeInBytes;
                });
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
            })
            .AddInterceptor<GrpcClientContextInterceptor>();
    }
}
