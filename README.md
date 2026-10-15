# ASP.NET Core Microservices Global Starter Kit (.NET 10)

Ushbu repozitoriy zamonaviy, yuqori yuklamali va kengaytiriluvchan korporativ mikroservis loyihalari uchun mo'ljallangan **Global Starter Kit** (arxitektura shabloni) hisoblanadi.

Loyiha hech qanday aniq biznes domeniga (eKadr, CRM, ERP, E-commerce va h.k.) bog'lanmagan bo'lib, ixtiyoriy korxona tizimini noldan toza, xavfsiz va yuqori unumdorlik bilan boshlash imkonini beradi.

---

## 🚀 Texnologik Stek

- **Platform:** .NET 10 (C# 14 / latest)
- **Paket boshqaruvi:** Central Package Management (CPM) — `Directory.Packages.props`
- **Arxitektura oqimi:** Pragmatic Direct Flow (`Controller -> Service -> DbContext`) — *No CQRS, No Repository Pattern*
- **Sinxron RPC:** Code-First gRPC (`protobuf-net.Grpc`) + Avtomatik Context/Trace propagation va Deadline Interceptors
- **Asinxron xabarlar:** RabbitMQ + MassTransit + PostgreSQL EF Core Transactional Outbox/Inbox
- **Ma'lumotlar bazasi:** PostgreSQL + EF Core 10 (`AddMicroserviceDbContext`, Snake_Case Naming, Global Query Filter, `xmin` Concurrency Token)
- **Ko'p ijarachilik (Multi-Tenancy):** `ITenantContext`, `ICurrentUser` claimlar asosida to'liq avtomatlashtirilgan izolyatsiya va instance-bound filtrlar
- **Kesh:** .NET 10 `HybridCache` (L1 In-Memory + L2 Redis) + Single-flight (Lock-on-Read) stampede himoyasi
- **Taqsimlangan qulflash:** Redis RedLock (`IDistributedLock`)
- **Fayllar xizmati:** MinIO (S3 API) + Pre-signed URLs + Tenant yo'l izolyatsiyasi
- **API Gateway:** YARP 2.3 (Yet Another Reverse Proxy) + CORS + Security Headers + Liveness/Readiness Health Checks
- **Xavfsizlik:** JWT Bearer (Asimmetrik / JWKS va Simmetrik) + Session Revocation Middleware (L1 + Redis) + RBAC (`[HasPermission]`)
- **Himoya vositalari:** Partitioned Rate Limiting (User / IP), HTTP Idempotency-Key deduplication (Redis Distributed Lock bilan), Brotli/Gzip siqish
- **Resilience:** Polly v8 (`Microsoft.Extensions.Http.Resilience`) — Standard Resilience Handler
- **Observability:** Serilog (`AppLoggerExtensions` orqali `ServiceName`, `ClassName`, `MethodName` va `LineNumber` to'liq compiler-generated / 0 StackTrace usulda yoziladi) + OpenTelemetry OTLP Tracing

---

## 📁 Loyiha Strukturasi

| Papka / Loyiha | Tavsif |
|---|---|
| `Common/Microservices.Common` | Barcha shartnomalar, bazaviy modellar (`BaseEntity`, `AggregateRoot`), xatoliklar (`AppException`), dinamik Expression filtrlash, `BaseDbContext`, outbox interceptori. |
| `WebCore/Microservices.WebCore` | Barcha HTTP/gRPC infratuzilmasi, middleware'lar (CorrelationId, Idempotency, GlobalException, SessionValidation), HybridTenantCache, RedisLock, MinIO, Serilog, gRPC interceptorlar. |
| `Integration/Microservices.Integration` | Tashqi uchinchi tomon tizimlari bilan integratsiyalar uchun resilient HttpClient bazasi (Polly v8 standard resilience handler). |
| `Gateway/Microservices.Gateway` | YARP (Yet Another Reverse Proxy) asosidagi zamonaviy va yuqori unumdor API Gateway. |
| `Services/Template/` | Namunaviy bo'sh mikroservis (`Template.Contracts` va `Template.Service`). |

---

## ⚡ Tezkor Boshlash (Quick Start)

### 1. Infratuzilmani ishga tushirish (Docker)
Loyiha ildiz papkasida barcha kerakli xizmatlarni (PostgreSQL, Redis, RabbitMQ, MinIO, Seq) bitta buyruq bilan ishga tushiring:

```bash
docker compose up -d
```

Xizmatlar portlari:
- **PostgreSQL:** `localhost:5432` (`postgres` / `postgrespassword`)
- **Redis:** `localhost:6379`
- **RabbitMQ UI:** `http://localhost:15672` (`guest` / `guest`)
- **MinIO Console:** `http://localhost:9001` (`minioadmin` / `minioadmin`)
- **Seq Log Viewer:** `http://localhost:5341`

### 2. Loyihani kompilyatsiya qilish
```bash
dotnet build Microservices.slnx
```

### 3. Gateway yoki servislarni ishga tushirish
Namunaviy servisni ishga tushirish:
```bash
dotnet run --project Services/Template/Template.Service/Microservices.Template.Service.csproj
```
Swagger UI: `http://localhost:5001/swagger`

Gateway'ni ishga tushirish:
```bash
dotnet run --project Gateway/Microservices.Gateway/Microservices.Gateway.csproj
```
Gateway manzili: `http://localhost:5000`

---

## 🛠️ Yangi Mikroservis Yaratish

Yangi mikroservis yaratish uchun tayyor `Services/Template` namunasidan nusxa olinadi:
1. `Services/[ServiceName]/[ServiceName].Contracts` (DTOlar, gRPC interfeys, hodisalar va istisnolar).
2. `Services/[ServiceName]/[ServiceName].Service` (`AddMicroserviceDbContext`, `MassTransit Outbox`, `gRPC Code-First`, `Controller`, `Service`, `Program.cs`, `appsettings.json`, `Dockerfile`).
3. Yangi loyihalarni `Microservices.slnx` ga qo'shish:
   ```bash
   dotnet sln Microservices.slnx add Services/[ServiceName]/[ServiceName].Contracts
   dotnet sln Microservices.slnx add Services/[ServiceName]/[ServiceName].Service
   ```

---

## 🧩 Kodlash Standartlari va Namunalari

### 1. Controller yaratish
Barcha controllerlar `BaseApiController` dan meros oladi va avtomatik ravishda `CorrelationId` bilan boyitilgan `ApiResponse<T>` qaytaradi:

```csharp
[Route("api/v1/items")]
public class ItemsController(ItemService itemService) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<ItemDto>>>> GetList(
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        var result = await itemService.GetPagedAsync(request, null, ct);
        return OkResponse(result);
    }
}
```

### 2. Servislararo gRPC Code-First chaqiruvi
Boshqa servisning faqat `.Contracts` kutubxonasiga murojaat qilasiz:

```csharp
// DI ro'yxatdan o'tkazish (Program.cs / Extension):
services.AddMicroserviceGrpcClient<IBillingGrpcService>("http://localhost:5003");

// Servis ichida ishlatish:
var billingData = await billingGrpcService.GetByIdAsync(
    new GetBillingByIdRequest { Id = orderId },
    new CallContext(cancellationToken));
```
`CorrelationId`, `TenantId`, `UserId` va 10 soniyalik `Deadline` avtomatik tarzda uzatiladi.

### 3. Asinxron hodisa tarqatish (Transactional Outbox)
```csharp
await publishEndpoint.Publish(new OrderCreatedIntegrationEvent
{
    OrderId = order.Id,
    TotalAmount = order.TotalAmount
}, cancellationToken);

// Baza bilan birga bitta tranzaksiyada saqlanadi:
await dbContext.SaveChangesAsync(cancellationToken);
```

Batafsil arxitektura qoidalari bilan [ARCHITECTURE.md](file:///home/abdumannon/Desktop/work/aspnet-microservices/ARCHITECTURE.md) faylida tanishishingiz mumkin.
