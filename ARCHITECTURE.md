# ASP.NET Core Enterprise Mikroservis Arxitekturasi Standarti

Ushbu arxitektura yuqori yuklamali (high-load), kengaytiriluvchan (scalable), ishonchli (resilient) va ko'p ijarachili (multi-tenant) enterprise tizimlar uchun mo'ljallangan global starter kit hisoblanadi.

---

## 1. Arxitektura Tamoyillari (Architecture Principles)

### 1.1. Ma'lumotlar Bazasi Izolyatsiyasi (Database-per-Service)
- Har bir mikroservis faqat o'z ma'lumotlar bazasiga ega bo'ladi (Database-per-Service pattern).
- Servislarning boshqa servis bazasiga to'g'ridan-to'g'ri ulanishi qat'iyan man etiladi.
- Baza sifatida faqat **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`) ishlatiladi.
- Barcha `DbContext` lar Singleton `NpgsqlDataSource` orqali ulanadi (global statement auto-preparation, dynamic multiplexing, `MinPoolSize = 10`, `KeepAlive = 30s` va connection pool tuning). To'g'ridan-to'g'ri string fallback taqiqlangan, to'liq `NpgsqlDataSource` talab qilinadi.
- Katta jadvallarda Cartesian Product ko'payib ketishining oldini olish uchun so'rovlarda `QuerySplittingBehavior.SplitQuery` sukut bo'yicha yoqilgan.

### 1.2. Servis Ichki Qatlami: Pragmatic Direct Flow (No CQRS, No Repository)
- **Repository Pattern taqiqlanadi:** EF Core `DbContext` ning o'zi repository va unit-of-work hisoblanadi. Keraksiz abstraksiya qatlamlari qo'shilmaydi.
- **CQRS / MediatR taqiqlanadi:** Yuzlab keraksiz Handler, Command, Query fayllari o'rniga to'g'ridan-to'g'ri oqim qo'llaniladi:
  `Controller -> Domain Service -> DbContext`.
- Bu throughput (o'tkazuvchanlik)ni maksimal darajaga ko'taradi, xotira (GC) bosimini pasaytiradi va kodni o'qish hamda debug qilishni osonlashtiradi.

### 1.3. Servislararo Aloqa (Inter-Service Communication)
- **Sinxron RPC (gRPC Code-First):**
  - Servislararo sinxron so'rovlar uchun `protobuf-net.Grpc` ishlatiladi (qo'lda `.proto` fayllar yozilmaydi).
  - Shartnomalar `[ServiceContract]`, `[OperationContract]`, `[ProtoContract]`, `[ProtoMember]` atributlari orqali yoziladi.
  - Har bir servis o'zining yengil **`[ServiceName].Contracts`** loyihasiga ega bo'ladi.
  - Cascading failure ning oldini olish uchun har bir gRPC chaqiruvida majburiy **Deadline (Timeout: 10 soniya)** va `CancellationToken` ishlatiladi.
  - `GrpcClientContextInterceptor` va `GrpcServerContextInterceptor` orqali `CorrelationId`, `TenantId`, `UserId` va `traceparent` avtomatik uzatiladi.
  - `GrpcExceptionInterceptor` orqali `AppException` avtomatik tarzda tegishli `RpcException` ga o'giriladi.
  - **gRPC Payload & SocketsHttpHandler Tuning:** gRPC server va clientlarida maksimal xabar hajmi `16 MB` ga oshirilgan, response kompressiyasi (`Fastest`) faollashtirilgan, `EnableMultipleHttp2Connections = true` va KeepAlive Ping sozlanib, yuqori yuklamada HTTP/2 stream saturaatsiyasi va kechikishlarning oldi olinadi.
  - Servislararo ichki HTTP/REST chaqiruvlar taqiqlanadi.
- **Asinxron Hodisalar (RabbitMQ + MassTransit + Transactional Outbox):**
  - Barcha asinxron jarayonlar va bildirishnomalar **RabbitMQ** (MassTransit) orqali event-driven (Pub/Sub) ko'rinishida yuboriladi.
  - Ma'lumotlar yo'qolishining oldini olish uchun **PostgreSQL EF Core Transactional Outbox/Inbox** patterni ishlatiladi.
  - Xabarlar uchun eksponentsial qayta urinish (exponential retry: 1s, 2s, 4s...) o'rnatilgan.
  - **Consumer Concurrency & Dead-Letter (Poison Message) Handling:** `RabbitMqOptions` orqali `PrefetchCount` (default 32) va `ConcurrentMessageLimit` (default 16) sozlanadi; muvaffaqiyatsiz tugagan xabarlar uchun umumiy `BaseFaultConsumer<T>` orqali audit va xavfsiz qayta ishlash qo'llab-quvvatlanadi.
  - **Multi-Tenancy Context Propagation:** `MassTransitTenantPublishFilter`, `MassTransitTenantSendFilter` va `MassTransitTenantConsumeFilter` orqali `TenantId` va `TenantCode` avtomatik ravishda RabbitMQ sarlavhalari (headers) orqali uzatiladi va qabul qiluvchi consumerda tiklanadi.

### 1.4. Ko'p Ijarachilik va Ma'lumotlar Izolyatsiyasi (Multi-Tenancy)
- `Common` loyihasida markaziy `ITenantContext` va `ICurrentUser` interfeyslari joylashgan bo'lib, ular JWT token claimlaridan joriy ijarachi va foydalanuvchini aniqlaydi.
- Fon vazifalari, consumerlar va integratsiya jarayonlarida ijarachini vaqtinchalik xavfsiz almashtirish uchun `tenantContext.Change(tenantId, tenantCode)` tokeni (`AsyncLocal`) mavjud.
- Har bir mikroservisning `DbContext` ida **EF Core Global Query Filter** o'rnatiladi. Filterlar `BaseDbContext` instansiyasiga xavfsiz bog'langan bo'lib, model keshining buzilishi va ijarachilararo ma'lumot sizishining oldini oladi.
- **Avtomatik Composite Index:** Barcha `ITenantEntity` va `ISoftDeletable` entitiylar uchun `(TenantId, IsDeleted)` B-Tree composite indeksi avtomatik yaratiladi, bu PostgreSQL sequential scan (to'liq jadvalni skanerlash) muammosini 100% bartaraf etadi.
- Mikroservis `DbContext` lari `AddMicroserviceDbContext<TDbContext>` orqali Scoped lifecycleda ro'yxatdan o'tkaziladi (`AddMicroserviceDbContextPool` multi-tenant xavfsizligi tufayli tavsiya etilmaydi).
- PostgreSQL da optimist qulflash uchun `xmin` system column concurrency token (`xid`) avtomatik ravishda barcha `BaseEntity` larda konfiguratsiya qilinadi.
- Domen ob'ektlaridagi `IDomainEvent` lar `BaseEntity.AddDomainEvent` orqali yig'iladi va `AuditSaveChangesInterceptor` orqali avtomatik Transactional Outbox orqali e'lon qilinadi.
- Ierarxik tuzilma qo'llab-quvvatlanadi: `Tenant` -> `District` -> `Region` -> `Global`.
- Fon vazifalari yoki umumiy hisobotlar uchun `using (tenantContext.DisableTenantFilter())` orqali faqat ijarachi filtrini vaqtincha ochish mumkin (`IsDeleted = false` soft-delete filtri esa doim faol qoladi).

### 1.5. Kesh Arxitekturasi (HybridCache: L1 + L2)
- .NET 10 rasmiy **HybridCache** (L1 In-Memory + L2 Redis) qo'llaniladi.
- `ITenantCache` wrapperi orqali kesh kalitlari avtomatik ravishda tenant prefiksi bilan nomlanadi (`tenant:{id}:{service}:{key}`), bu tenantlararo kesh aralashuvini 100% bartaraf etadi.
- Teglar (`tags`) orqali bir vaqtning o'zida bir nechta bog'liq kesh yozuvlarini bekor qilish (`RemoveByTagAsync`) va to'g'ridan-to'g'ri qiymat yozish (`SetAsync`) qo'llab-quvvatlanadi.
- `HybridCache.GetOrCreateAsync` ichki **Lock-on-Read (Single-Flight)** mexanizmi orqali Cache Stampede (Dog-Piling) dan himoyalaydi.

### 1.6. Taqsimlangan Qulflash (Distributed Lock - RedLock & Watchdog)
- Redis asosidagi `IDistributedLock` parallel to'qnashuvlar (race condition) va bir vaqtning o'zida bir xil resursni o'zgartirishdan himoya qiladi.
- **Heartbeat/Watchdog:** Uzoq davom etadigan operatsiyalar paytida qulf muddatidan oldin tugab qolmasligi uchun `PeriodicTimer` yordamida avtomatik uzaytirish (heartbeat auto-renewal) amalga oshiriladi.

### 1.7. Fayllar bilan Ishlash (MinIO / S3 Object Storage & Stream Pooling)
- Fayllar server diskiga emas, to'liq **MinIO (S3 API)** ga yuklanadi.
- Katta fayllar bilan xotira yukini kamaytirish uchun to'g'ridan-to'g'ri oqim (`UploadStreamAsync`, `DownloadStreamAsync`) qo'llaniladi (Large Object Heap LOH bosimi nolga tushirilgan).
- `Microsoft.IO.RecyclableMemoryStream` poolidan foydalanilib, bayt massivlari o'rtasidagi bufer ko'chirishlarida Gen2 GC to'xtashlari va LOH fragmentatsiyasi butunlay bartaraf etilgan.
- Xavfsizlik uchun **Pre-signed URL** lardan foydalaniladi (5-15 daqiqa TTL).
- Fayllar yo'li tenant izolyatsiyasida saqlanadi: `{tenant_id}/{entity_name}/{year}/{file_guid}.{ext}`.

### 1.8. Yagona API Response Standarti & JSON Serilizatsiya
Barcha API javoblari yagona umumiy formatda qaytadi:
```json
{
  "success": true,
  "code": 200,
  "message": null,
  "data": { ... },
  "correlationId": "4c94fb2188ab4d38be4910cfce1dfa30"
}
```
Sahifalash uchun:
```json
{
  "success": true,
  "code": 200,
  "message": null,
  "data": {
    "items": [ ... ],
    "total": 150
  },
  "correlationId": "4c94fb2188ab4d38be4910cfce1dfa30"
}
```
- JSON serilizatsiyasi uchun standart, o'ta tezyurar `System.Text.Json` ishlatiladi. Agar kelajakda Native AOT (JIT-siz binary) yoki ultra-yuqori RPS talab qilinsa, opt-in sifatida C# Source Generator namunasidan (`Common/Serialization/SampleJsonSerializerContext.cs`) foydalanish mumkin.

### 1.9. Dinamik Filtrlash, Saralash va Sahifalash (QueryableFilterExtensions)
- Dictionary asosida dinamik filtrlash qoidalari (`==`, `<>`, `>=`, `<=`, `between`, `like`, `ilike`, `in`, `nin`).
- `ConcurrentDictionary` da keshlanuvchi va parametrlashtirilgan EF Core Expression Tree SQL Injectiondan to'liq himoyalaydi va yuqori tezlikni ta'minlaydi.
- PostgreSQL `EF.Functions.ILike` / `EF.Functions.Like` hamda to'g'ridan-to'g'ri massiv `Contains` metodlaridan samarali foydalaniladi.
- Read-only so'rovlar uchun `ToReadOnlyListAsync()` va `AsNoTracking()` kengaytmalari orqali EF Core Change Tracker xotira yuklamasi sezilarli darajada qisqartiriladi.

### 1.10. Audit va Non-Repudiation (AuditSaveChangesInterceptor)
- Har bir `DbContext.SaveChangesAsync()` chaqirilganda `IAuditableEntity` va `ISoftDeletable` maydonlari avtomatik to'ldiriladi.
- O'zgarishlar (`OldValues`, `NewValues`, `UserId`, `ClientIP`) tranzaksiya bazaga commit bo'lgach (`SavedChangesAsync`), avtomatik tarzda **MassTransit Transactional Outbox** orqali `EntityAuditedEvent` sifatida navbatga chiqariladi (asosiy jarayonga nol kechikish ta'siri bilan).

### 1.11. Xavfsizlik, Idempotentlik va Himoya
- **HTTP Idempotency-Key Middleware:** Takroriy POST/PUT so'rovlarida Redis `IDistributedLock` orqali parallel dublikat chaqiruvlarni bloklaydi va faqat muvaffaqiyatli ($2xx$) javoblarni keshlaydi.
- **Session / Token Revocation Middleware:** L1 In-Memory (20 soniya) + L2 Redis orqali yaroqsiz qilingan sessiyalarni bir zumda aniqlab, `401 Unauthorized ("token_revoked")` qaytaradi.
- **Taqsimlangan Rate Limiting:** Gateway va WebCore qatlamlarida `PartitionedRateLimiter` orqali foydalanuvchi (`user:{userId}`) yoki IP (`ip:{clientIp}`) bo'yicha alohida darcha asosida himoyalaydi.
- **Resilience:** Polly v8 (`Microsoft.Extensions.Http.Resilience`) asosidagi `AddStandardResilienceHandler` orqali qayta urinish, timeout va circuit-breaker himoyasi.
- **Health Checks & Monitoring:** `/health/live` (liveness), `/health/ready` (batafsil JSON formatda DB va Redis tayyorligi va millisekundlardagi kechikish hisoboti), hamda YARP Gateway Active Health Check bilan avtomatik failover.
- **Siqish (Compression):** Brotli (asosiy) + Gzip (fallback) orqali tarmoq trafigini siqish (gRPC binary oqimlarini buzmagan holda).
- **OpenTelemetry:** OTLP protokoli orqali so'rovlar zanjirini kuzatish (MassTransit, Npgsql, HttpClient, AspNetCore tracing va Runtime/Process metrics).
- **Serilog Logging:** `AppLoggerExtensions` (`LogInfo`, `LogWarn`, `LogError`, `LogDebug`) orqali log yozgan servis nomi (`ServiceName`), sinf nomi (`ClassName`), metod nomi (`MethodName`) va manba qatori (`LineNumber`) to'liq kompilyatsiya vaqtida (Compiler-Generated, 0 StackTrace) `[ServiceName] [ClassName.MethodName:LineNumber]` formatida muhrlanadi.

---

## 2. Loyiha Strukturasi (Solution Layout)

```text
aspnet-microservices/
├── Directory.Build.props            # Global .NET 10, C# latest, NoWarn sozlamalari
├── Directory.Packages.props         # Central Package Management (CPM) barcha paketlar versiyasi
├── Microservices.slnx               # .NET 10 Solution fayli
├── Common/                          # Microservices.Common: Barcha domen, shartnoma va bazaviy turlar
│   ├── Cache/                       # ITenantCache
│   ├── Constants/                   # ConnectionNames, AppClaimTypes, AppHeaderNames
│   ├── Context/                     # ITenantContext, ICurrentUser, SystemCurrentUser
│   ├── Domain/                      # BaseEntity, AggregateRoot, IDomainEvent, IAuditableEntity, ISoftDeletable
│   ├── Exceptions/                  # AppException, ErrorType, NotFoundException, ConflictException, etc.
│   ├── Filtering/                   # FilterRule, FilterCondition, QueryableFilterExtensions
│   ├── Jobs/                        # IBackgroundJob
│   ├── Lock/                        # IDistributedLock
│   ├── Models/                      # ApiResponse, PagedResult, PagedRequest
│   ├── Options/                     # RabbitMqOptions, IdentityOptions, MinioOptions, etc.
│   ├── Persistence/                 # BaseDbContext, TenantQueryFilterExtensions
│   │   └── Interceptors/            # AuditSaveChangesInterceptor, IAuditOutboxPublisher
│   ├── Serialization/               # SampleJsonSerializerContext (Opt-in Native AOT reference)
│   └── Storage/                     # IFileStorageService
├── WebCore/                         # Microservices.WebCore: Barcha HTTP, gRPC va infra middleware/extensionlar
│   ├── Audit/                       # MassTransitAuditPublisher
│   ├── Cache/                       # HybridTenantCache
│   ├── Context/                     # CurrentUserHttpContext, TenantContextHttpContext, MassTransitTenantFilters
│   ├── Controllers/                 # BaseApiController
│   ├── Extensions/                  # ServiceInfrastructureExtensions, WebCoreExtensions, Compression, OpenTelemetry...
│   ├── HealthChecks/                # RedisHealthCheck
│   ├── Interceptors/                # GrpcClientContextInterceptor, GrpcServerContextInterceptor, GrpcException...
│   ├── Lock/                        # RedisDistributedLock (Watchdog auto-renewal)
│   ├── Logging/                     # CallerMethodEnricher, AppLogContext, AppLoggerExtensions
│   ├── Messaging/                   # BaseFaultConsumer (Dead-letter & poison message handling)
│   ├── Middleware/                  # CorrelationIdMiddleware, GlobalExceptionMiddleware, Idempotency...
│   ├── OpenApi/                     # BearerSecuritySchemeTransformer
│   ├── Security/                    # HasPermissionAttribute, PermissionAuthorizationHandler
│   └── Storage/                     # MinioFileStorageService (Stream-based & RecyclableMemoryStream)
├── Integration/                     # Microservices.Integration: Tashqi tizimlar mijozi bazasi (Polly v8, dual-mode)
│   └── Shared/                      # BaseIntegrationClient, BaseIntegrationOptions, IntegrationPolicies
├── Gateway/                         # Microservices.Gateway: YARP (Reverse Proxy) API Gateway (Rate Limiting, Health Checks)
│   └── Microservices.Gateway/       # Program.cs, appsettings.json, Dockerfile
└── Services/                        # Barcha mikroservislar
    └── Template/                    # Generic namunaviy mikroservis
        ├── Template.Contracts/      # DTOs, gRPC Interfaces, Events, Exceptions
        └── Template.Service/        # Domain, Persistence, Services, Grpc, Controllers, Program.cs, Dockerfile
```

---

## 3. Yangi Mikroservis Yaratish Standarti

Har qanday yangi mikroservis 2 ta alohida loyihaga bo'linadi:

1. **`[ServiceName].Contracts`**:
   - Yengil sinf kutubxonasi (`Class Library`).
   - Faqat DTO lar (`[ProtoContract]`), gRPC interfeyslar (`[ServiceContract]`), hodisalar (`IIntegrationEvent`) va maxsus istisnolarni o'z ichiga oladi.
   - Boshqa istalgan mikroservis faqat ushbu `.Contracts` loyihasini reference qiladi (hech qachon to'liq servisni emas!).

2. **`[ServiceName].Service`**:
   - Web API va gRPC server loyihasi (`Microsoft.NET.Sdk.Web`).
   - Domen modellari (`AggregateRoot<long>`), `DbContext`, domen xizmatlari (`Service`), `Controller` lar va `Grpc` implementatsiyalarini o'z ichiga oladi.
