#!/usr/bin/env bash
set -e

if [ -z "$1" ]; then
  echo "Usage: ./scripts/new-service.sh <ServiceName>"
  echo "Example: ./scripts/new-service.sh Billing"
  exit 1
fi

SERVICE_NAME="$1"
# Lowercase for configs and URLs
SERVICE_LOWER=$(echo "$SERVICE_NAME" | tr '[:upper:]' '[:lower:]')

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_DIR="$ROOT_DIR/Services/$SERVICE_NAME"
CONTRACTS_DIR="$SERVICE_DIR/$SERVICE_NAME.Contracts"
SERVICE_IMPL_DIR="$SERVICE_DIR/$SERVICE_NAME.Service"

echo "Scaffolding new microservice: $SERVICE_NAME..."

mkdir -p "$CONTRACTS_DIR/Dtos" "$CONTRACTS_DIR/Services" "$CONTRACTS_DIR/Events" "$CONTRACTS_DIR/Exceptions"
mkdir -p "$SERVICE_IMPL_DIR/Domain" "$SERVICE_IMPL_DIR/Persistence" "$SERVICE_IMPL_DIR/Services" "$SERVICE_IMPL_DIR/Grpc" "$SERVICE_IMPL_DIR/Controllers" "$SERVICE_IMPL_DIR/Extensions"

# 1. Contracts Project
cat <<EOF > "$CONTRACTS_DIR/Microservices.$SERVICE_NAME.Contracts.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Microservices.$SERVICE_NAME.Contracts</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../Common/Microservices.Common.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="protobuf-net" />
    <PackageReference Include="protobuf-net.Grpc" />
    <PackageReference Include="System.ServiceModel.Primitives" />
  </ItemGroup>
</Project>
EOF

cat <<EOF > "$CONTRACTS_DIR/Dtos/${SERVICE_NAME}Dtos.cs"
using ProtoBuf;

namespace Microservices.$SERVICE_NAME.Contracts.Dtos;

[ProtoContract]
public class ${SERVICE_NAME}Dto
{
    [ProtoMember(1)]
    public long Id { get; init; }

    [ProtoMember(2)]
    public string Title { get; init; } = string.Empty;

    [ProtoMember(3)]
    public string? Description { get; init; }

    [ProtoMember(4)]
    public DateTimeOffset CreatedAt { get; init; }
}

[ProtoContract]
public class Create${SERVICE_NAME}Request
{
    [ProtoMember(1)]
    public string Title { get; init; } = string.Empty;

    [ProtoMember(2)]
    public string? Description { get; init; }
}

[ProtoContract]
public class Get${SERVICE_NAME}ByIdRequest
{
    [ProtoMember(1)]
    public long Id { get; init; }
}
EOF

cat <<EOF > "$CONTRACTS_DIR/Services/I${SERVICE_NAME}GrpcService.cs"
using System.ServiceModel;
using Microservices.$SERVICE_NAME.Contracts.Dtos;
using ProtoBuf.Grpc;

namespace Microservices.$SERVICE_NAME.Contracts.Services;

[ServiceContract]
public interface I${SERVICE_NAME}GrpcService
{
    [OperationContract]
    Task<${SERVICE_NAME}Dto> GetByIdAsync(Get${SERVICE_NAME}ByIdRequest request, CallContext context = default);
}
EOF

cat <<EOF > "$CONTRACTS_DIR/Events/${SERVICE_NAME}Events.cs"
using Microservices.Common.Domain;

namespace Microservices.$SERVICE_NAME.Contracts.Events;

public class ${SERVICE_NAME}CreatedIntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
    public long ${SERVICE_NAME}Id { get; init; }
    public string Title { get; init; } = string.Empty;
}
EOF

cat <<EOF > "$CONTRACTS_DIR/Exceptions/${SERVICE_NAME}Exceptions.cs"
using Microservices.Common.Exceptions;

namespace Microservices.$SERVICE_NAME.Contracts.Exceptions;

public class ${SERVICE_NAME}NotFoundException(long id)
    : NotFoundException("${SERVICE_LOWER}_not_found", [id]);
EOF

# 2. Service Implementation Project
cat <<EOF > "$SERVICE_IMPL_DIR/Microservices.$SERVICE_NAME.Service.csproj"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Microservices.$SERVICE_NAME.Service</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../Common/Microservices.Common.csproj" />
    <ProjectReference Include="../../../WebCore/Microservices.WebCore.csproj" />
    <ProjectReference Include="../${SERVICE_NAME}.Contracts/Microservices.${SERVICE_NAME}.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="EFCore.NamingConventions" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="MassTransit" />
    <PackageReference Include="MassTransit.RabbitMQ" />
    <PackageReference Include="MassTransit.EntityFrameworkCore" />
    <PackageReference Include="protobuf-net.Grpc.AspNetCore" />
  </ItemGroup>
</Project>
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/Domain/${SERVICE_NAME}Item.cs"
using System.ComponentModel.DataAnnotations;
using Microservices.Common.Domain;

namespace Microservices.$SERVICE_NAME.Service.Domain;

public class ${SERVICE_NAME}Item : AggregateRoot<long>, ITenantEntity
{
    public long? TenantId { get; set; }
    public long? RegionId { get; set; }
    public long? DistrictId { get; set; }
    public long? InstitutionId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }
}
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/Persistence/${SERVICE_NAME}DbContext.cs"
using Microservices.Common.Persistence;
using Microservices.$SERVICE_NAME.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace Microservices.$SERVICE_NAME.Service.Persistence;

public class ${SERVICE_NAME}DbContext(DbContextOptions<${SERVICE_NAME}DbContext> options) : BaseDbContext(options)
{
    public DbSet<${SERVICE_NAME}Item> ${SERVICE_NAME}Items => Set<${SERVICE_NAME}Item>();
}
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/Services/${SERVICE_NAME}Service.cs"
using MassTransit;
using Microservices.Common.Context;
using Microservices.Common.Filtering;
using Microservices.Common.Models;
using Microservices.$SERVICE_NAME.Contracts.Dtos;
using Microservices.$SERVICE_NAME.Contracts.Events;
using Microservices.$SERVICE_NAME.Contracts.Exceptions;
using Microservices.$SERVICE_NAME.Service.Domain;
using Microservices.$SERVICE_NAME.Service.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Microservices.$SERVICE_NAME.Service.Services;

public class ${SERVICE_NAME}Service(
    ${SERVICE_NAME}DbContext dbContext,
    IPublishEndpoint publishEndpoint,
    ITenantContext tenantContext)
{
    public async Task<PagedResult<${SERVICE_NAME}Dto>> GetPagedAsync(
        PagedRequest request,
        IDictionary<string, FilterRule>? filters,
        CancellationToken ct = default)
    {
        var pagedEntities = await dbContext.${SERVICE_NAME}Items
            .AsNoTracking()
            .ToPagedResultAsync(request, filters, ct);

        var dtos = pagedEntities.Items.Select(MapToDto).ToList();
        return new PagedResult<${SERVICE_NAME}Dto>(dtos, pagedEntities.Total);
    }

    public async Task<${SERVICE_NAME}Dto> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var item = await dbContext.${SERVICE_NAME}Items
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ${SERVICE_NAME}NotFoundException(id);

        return MapToDto(item);
    }

    public async Task<${SERVICE_NAME}Dto> CreateAsync(Create${SERVICE_NAME}Request request, CancellationToken ct = default)
    {
        var item = new ${SERVICE_NAME}Item
        {
            TenantId = tenantContext.TenantId,
            Title = request.Title,
            Description = request.Description
        };

        dbContext.${SERVICE_NAME}Items.Add(item);

        await publishEndpoint.Publish(new ${SERVICE_NAME}CreatedIntegrationEvent
        {
            ${SERVICE_NAME}Id = item.Id,
            Title = item.Title
        }, ct);

        await dbContext.SaveChangesAsync(ct);

        return MapToDto(item);
    }

    private static ${SERVICE_NAME}Dto MapToDto(${SERVICE_NAME}Item entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Description = entity.Description,
        CreatedAt = entity.CreatedAt
    };
}
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/Grpc/${SERVICE_NAME}GrpcService.cs"
using Microservices.$SERVICE_NAME.Contracts.Dtos;
using Microservices.$SERVICE_NAME.Contracts.Services;
using Microservices.$SERVICE_NAME.Service.Services;
using ProtoBuf.Grpc;

namespace Microservices.$SERVICE_NAME.Service.Grpc;

public class ${SERVICE_NAME}GrpcService(${SERVICE_NAME}Service service) : I${SERVICE_NAME}GrpcService
{
    public async Task<${SERVICE_NAME}Dto> GetByIdAsync(Get${SERVICE_NAME}ByIdRequest request, CallContext context = default)
    {
        return await service.GetByIdAsync(request.Id, context.CancellationToken);
    }
}
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/Controllers/${SERVICE_NAME}Controller.cs"
using Microservices.Common.Filtering;
using Microservices.Common.Models;
using Microservices.$SERVICE_NAME.Contracts.Dtos;
using Microservices.$SERVICE_NAME.Service.Services;
using Microservices.WebCore.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Microservices.$SERVICE_NAME.Service.Controllers;

[Route("api/v1/${SERVICE_LOWER}s")]
public class ${SERVICE_NAME}Controller(${SERVICE_NAME}Service service) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<${SERVICE_NAME}Dto>>>> GetList(
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        var result = await service.GetPagedAsync(request, null, ct);
        return OkResponse(result);
    }

    [HttpPost("search")]
    public async Task<ActionResult<ApiResponse<PagedResult<${SERVICE_NAME}Dto>>>> Search(
        [FromQuery] PagedRequest request,
        [FromBody] Dictionary<string, FilterRule>? filters,
        CancellationToken ct)
    {
        var result = await service.GetPagedAsync(request, filters, ct);
        return OkResponse(result);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<${SERVICE_NAME}Dto>>> GetById(long id, CancellationToken ct)
    {
        var item = await service.GetByIdAsync(id, ct);
        return OkResponse(item);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<${SERVICE_NAME}Dto>>> Create(
        [FromBody] Create${SERVICE_NAME}Request request,
        CancellationToken ct)
    {
        var created = await service.CreateAsync(request, ct);
        return CreatedResponse($"/api/v1/${SERVICE_LOWER}s/{created.Id}", created);
    }
}
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/Extensions/ServiceCollectionExtensions.cs"
using Microservices.$SERVICE_NAME.Service.Persistence;
using Microservices.$SERVICE_NAME.Service.Services;
using Microservices.WebCore.Extensions;

namespace Microservices.$SERVICE_NAME.Service.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection Add${SERVICE_NAME}Infrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMicroserviceDbContextPool<${SERVICE_NAME}DbContext>(configuration);
        services.AddMicroserviceMassTransit<${SERVICE_NAME}DbContext>(configuration);
        services.AddMicroserviceGrpcServer();

        return services;
    }

    public static IServiceCollection Add${SERVICE_NAME}ApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<${SERVICE_NAME}Service>();

        return services;
    }
}
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/Program.cs"
using Microservices.$SERVICE_NAME.Service.Extensions;
using Microservices.$SERVICE_NAME.Service.Grpc;
using Microservices.WebCore.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWebCore(builder.Configuration, "${SERVICE_LOWER}-service");
builder.Services.Add${SERVICE_NAME}Infrastructure(builder.Configuration);
builder.Services.Add${SERVICE_NAME}ApplicationServices();

var app = builder.Build();

app.UseWebCore();
app.MapGrpcService<${SERVICE_NAME}GrpcService>();

app.Run();
EOF

cat <<EOF > "$SERVICE_IMPL_DIR/appsettings.json"
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=microservices_${SERVICE_LOWER};Username=postgres;Password=postgrespassword",
    "Redis": "localhost:6379"
  },
  "RabbitMQ": {
    "Host": "localhost",
    "Port": 5672,
    "Username": "guest",
    "Password": "guest"
  },
  "Identity": {
    "Issuer": "https://identity.local",
    "Audience": "microservices",
    "JwksUri": "http://localhost:5001/.well-known/jwks.json"
  },
  "Service": {
    "Name": "${SERVICE_LOWER}-service",
    "Version": "1.0.0"
  }
}
EOF

# Add to solution if solution exists
SLN_FILE=""
if [ -f "$ROOT_DIR/Microservices.slnx" ]; then
  SLN_FILE="$ROOT_DIR/Microservices.slnx"
elif [ -f "$ROOT_DIR/Microservices.sln" ]; then
  SLN_FILE="$ROOT_DIR/Microservices.sln"
fi

if [ -n "$SLN_FILE" ]; then
  dotnet sln "$SLN_FILE" add "$CONTRACTS_DIR/Microservices.$SERVICE_NAME.Contracts.csproj" --solution-folder "Services/$SERVICE_NAME"
  dotnet sln "$SLN_FILE" add "$SERVICE_IMPL_DIR/Microservices.$SERVICE_NAME.Service.csproj" --solution-folder "Services/$SERVICE_NAME"
fi

echo "Microservice $SERVICE_NAME successfully created!"
