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
- **Ma'lumotlar bazasi:** PostgreSQL + EF Core 10 (`AddDbContextPool`, Snake_Case Naming, Global Query Filter)
- **Ko'p ijarachilik (Multi-Tenancy):** `ITenantContext`, `ICurrentUser` claimlar asosida to'liq avtomatlashtirilgan izolyatsiya
- **Kesh:** .NET 10 `HybridCache` (L1 In-Memory + L2 Redis) + Single-flight (Lock-on-Read) stampede himoyasi
- **Taqsimlangan qulflash:** Redis RedLock (`IDistributedLock`)
- **Fayllar xizmati:** MinIO (S3 API) + Pre-signed URLs + Tenant yo'l izolyatsiyasi
- **API Gateway:** Ocelot 24 + CORS + Security Headers + Liveness/Readiness Health Checks
- **Xavfsizlik:** JWT Bearer (Asimmetrik / JWKS va Simmetrik) + Session Revocation Middleware (L1 + Redis) + RBAC (`[HasPermission]`)
- **Himoya vositalari:** Fixed-Window Rate Limiting, HTTP Idempotency-Key deduplication, Brotli/Gzip siqish
- **Observability:** Serilog (`CallerMethodEnricher` orqali `ServiceName` va `MethodName` avtomatik yoziladi) + OpenTelemetry OTLP Tracing

---

## 📁 Loyiha Strukturasi

| Papka / Loyiha | Tavsif |
|---|---|
| `Common/Microservices.Common` | Barcha shartnomalar, bazaviy modellar (`BaseEntity`, `AggregateRoot`), xatoliklar (`AppException`), dinamik Expression filtrlash, `BaseDbContext`, outbox interceptori. |
| `WebCore/Microservices.WebCore` | Barcha HTTP/gRPC infratuzilmasi, middleware'lar (CorrelationId, Idempotency, GlobalException, SessionValidation), HybridTenantCache, RedisLock, MinIO, Serilog, gRPC interceptorlar. |
| `Integration/Microservices.Integration` | Tashqi uchinchi tomon tizimlari bilan integratsiyalar uchun resilient HttpClient bazasi (Polly retry & circuit-breaker). |
| `Gateway/Microservices.Gateway` | Ocelot asosidagi markaziy API Gateway. |
| `Services/Template/` | Namunaviy bo'sh mikroservis (`Template.Contracts` va `Template.Service`). |
| `scripts/new-service.sh` | 1 soniyada yangi mikroservis generatsiya qiluvchi avtomatlashtirish skripti. |

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

## 🛠️ Yangi Mikroservis Yaratish (Scaffolding)

Yangi mikroservis yaratish uchun tayyor generator skriptidan foydalaning:

```bash
./scripts/new-service.sh Billing
```

Ushbu skript avtomatik ravishda:
1. `Services/Billing/Billing.Contracts` loyihasini (DTOs, gRPC interfeys, hodisalar va istisnolar bilan) yaratadi.
2. `Services/Billing/Billing.Service` loyihasini (`DbContextPool`, `MassTransit Outbox`, `gRPC Code-First`, `Controller`, `Service`, `Program.cs`, `appsettings.json`, `Dockerfile` bilan) yaratadi.
3. Yangi loyihalarni `Microservices.slnx` ga avtomatik qo'shadi.

Siz faqat o'z domen modullaringizni qo'shib, biznes mantiqni yozishni boshlaysiz!

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
