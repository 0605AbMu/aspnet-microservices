# ASP.NET Core Enterprise Mikroservis Arxitekturasi Standarti

Ushbu arxitektura yuqori yuklamali (high-load), kengaytiriluvchan (scalable), ishonchli (resilient) va ko'p ijarachili (multi-tenant) enterprise tizimlar uchun mo'ljallangan global starter kit hisoblanadi.

---

## 1. Arxitektura Tamoyillari (Architecture Principles)

### 1.1. Ma'lumotlar Bazasi Izolyatsiyasi (Database-per-Service)
- Har bir mikroservis faqat o'z ma'lumotlar bazasiga ega bo'ladi (Database-per-Service pattern).
- Servislarning boshqa servis bazasiga to'g'ridan-to'g'ri ulanishi qat'iyan man etiladi.
- Baza sifatida faqat **PostgreSQL** (`Npgsql.EntityFrameworkCore.PostgreSQL`) ishlatiladi.
- Barcha `DbContext` lar Singleton `NpgsqlDataSource` orqali ulanadi (global statement auto-preparation: `MaxAutoPrepare = 50`, `AutoPrepareMinUsages = 2`, dynamic multiplexing, `MinPoolSize = 10`, `KeepAlive = 30s` va connection pool tuning). To'g'ridan-to'g'ri string fallback taqiqlangan, to'liq `NpgsqlDataSource` talab qilinadi. Serverda SQL parsing va execution plan rejalashtirish xarajatlari kamaytirilgan. Ulanishga sukut bo'yicha `Options=-c random_page_cost=1.1` (SSD/NVMe qiymati) qo'shiladi: standart `4` da PostgreSQL `WHERE tenant_id = @t ORDER BY id DESC LIMIT n` uchun PK bo'ylab orqaga skanerlashni tanlab, eski tenantlarda millionlab begona qatorlarni o'qiydi (o'lchangan: 100 ming qatorli tenantda 160–400 ms → 0.8 ms). Connection string'da `Options=` aniq berilsa, o'sha ishlatiladi.
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
  - Cascading failure ning oldini olish uchun har bir gRPC chaqiruvida majburiy **Deadline (Timeout: 10 soniya)** va kiruvchi HTTP `RequestAborted` bilan bog'langan `CancellationToken` ishlatiladi (mijoz uzilsa, ichki gRPC ham darhol to'xtatiladi).
  - `GrpcClientContextInterceptor` va `GrpcServerContextInterceptor` orqali `CorrelationId`, `TenantId`, `UserId` va `traceparent` avtomatik uzatiladi.
  - `GrpcExceptionInterceptor` orqali `AppException` avtomatik tarzda tegishli `RpcException` ga o'giriladi.
  - **gRPC Payload & SocketsHttpHandler Tuning:** gRPC server va clientlarida maksimal xabar hajmi `16 MB` ga oshirilgan, response kompressiyasi (`Fastest`) faollashtirilgan, `EnableMultipleHttp2Connections = true` va KeepAlive Ping sozlanib, yuqori yuklamada HTTP/2 stream saturaatsiyasi va kechikishlarning oldi olinadi.
  - Servislararo ichki HTTP/REST chaqiruvlar taqiqlanadi.
- **Asinxron Hodisalar (RabbitMQ + MassTransit + Transactional Outbox):**
  - Barcha asinxron jarayonlar va bildirishnomalar **RabbitMQ** (MassTransit) orqali event-driven (Pub/Sub) ko'rinishida yuboriladi.
  - Ma'lumotlar yo'qolishining oldini olish uchun **PostgreSQL EF Core Transactional Outbox/Inbox** patterni ishlatiladi. Entity va Outbox xabarlarining 100% atomik saqlanishi uchun servis operatsiyalarida `dbContext.Transactional(...)` kengaytmasi orqali ikkala `SaveChangesAsync` bitta PostgreSQL tranzaksiyasiga birlashtiriladi (agar tashqi tranzaksiya allaqachon mavjud bo'lsa, xato bermasdan o'sha mavjud tranzaksiyada ishtirok etadi va qaytgan qiymatni `return` qiladi).
  - Xabarlar uchun eksponentsial qayta urinish (exponential retry: 1s, 2s, 4s...) o'rnatilgan.
  - **Outbox yetkazib berish:** `IsolationLevel = ReadCommitted` (standart `RepeatableRead` da `FOR UPDATE SKIP LOCKED` PostgreSQL `40001 could not serialize access` xatolariga olib kelardi; o'lchangan: bitta aylanma 66 → ~550 xabar/s). `RabbitMQ:OutboxDeliveryWorkers` (standart `1`) qo'shimcha parallel aylanmalar sonini belgilaydi, lekin ular so'rov yo'li bilan commit uchun raqobatlashadi, faqat o'lchab oshiring.
  - **Consumer Concurrency & Dead-Letter (Poison Message) Handling:** `RabbitMqOptions` orqali `PrefetchCount` (default 32) va `ConcurrentMessageLimit` (default 16) sozlanadi; muvaffaqiyatsiz tugagan xabarlar uchun umumiy `BaseFaultConsumer<T>` orqali audit va xavfsiz qayta ishlash qo'llab-quvvatlanadi.
  - **Multi-Tenancy Context Propagation:** `MassTransitTenantPublishFilter`, `MassTransitTenantSendFilter` va `MassTransitTenantConsumeFilter` orqali `TenantId` va `TenantCode` avtomatik ravishda RabbitMQ sarlavhalari (headers) orqali uzatiladi va qabul qiluvchi consumerda tiklanadi.

### 1.4. Ko'p Ijarachilik va Ma'lumotlar Izolyatsiyasi (Multi-Tenancy)
- `Common` loyihasida markaziy `ITenantContext` va `ICurrentUser` interfeyslari joylashgan bo'lib, ular JWT token claimlaridan joriy ijarachi va foydalanuvchini aniqlaydi.
- Fon vazifalari, consumerlar va integratsiya jarayonlarida ijarachini vaqtinchalik xavfsiz almashtirish uchun `tenantContext.Change(tenantId, tenantCode)` tokeni (`AsyncLocal`) mavjud.
- Har bir mikroservisning `DbContext` ida **EF Core Global Query Filter** o'rnatiladi. Filterlar `BaseDbContext` instansiyasiga xavfsiz bog'langan bo'lib, model keshining buzilishi va ijarachilararo ma'lumot sizishining oldini oladi.
- **Avtomatik tenant indeksi:** `ITenantEntity` + `ISoftDeletable` + `BaseEntity<long>` entitiylar uchun `(TenantId, Id) WHERE is_deleted = false` qisman B-Tree indeksi avtomatik yaratiladi. U tenant filtrini, standart `ORDER BY Id DESC` saralashni (orqaga skanerlash) va `LIMIT` ni bitta indeks oralig'idan qondiradi. Indeks ataylab o'suvchi (ASC): id'lar monoton o'sgani uchun yozuvlar har tenant oralig'ining oxiriga qo'shiladi va sahifalar to'la qoladi (DESC indeks ~50% to'lib, 1.5× kattalashadi).
- **Tenant filtri SQL'i:** global/o'chirilgan rejim (`Global` scope yoki `DisableTenantFilter()`) SQL'ga `EF.Constant` bilan konstanta sifatida kiritiladi, shuning uchun tenant rejimida so'rov oddiy `WHERE tenant_id = @t` bo'ladi (`OR @param` shakli har bajarilishda qayta rejalashtirishni talab qilardi va umumiy rejada indeksni ishlatolmasdi). `TenantModeCompiledQueryCacheKeyGenerator` kompilyatsiya qilingan so'rovlar keshini shu rejim bo'yicha ajratadi; u `BaseDbContext.OnConfiguring` da doim o'rnatiladi.
- **Id generatsiyasi:** `BaseEntity<long>.Id` PostgreSQL HiLo ketma-ketligidan (`id_hilo_seq`, blok = 100) olinadi. Id INSERT'dan oldin ma'lum bo'ladi, shuning uchun entity va uning outbox xabarlari bitta `SaveChanges` (bitta batch, bitta tranzaksiya) bilan saqlanadi; `DbSet.AddAsync` ishlatiladi.
- Mikroservis `DbContext` lari `AddMicroserviceDbContext<TDbContext>` orqali Scoped lifecycleda ro'yxatdan o'tkaziladi (`AddMicroserviceDbContextPool` multi-tenant xavfsizligi tufayli tavsiya etilmaydi).
- PostgreSQL da optimist qulflash uchun `xmin` system column concurrency token (`xid`) avtomatik ravishda barcha `BaseEntity` larda konfiguratsiya qilinadi.
- Domen ob'ektlaridagi `IDomainEvent` lar `BaseEntity.AddDomainEvent` orqali yig'iladi va `AuditSaveChangesInterceptor` orqali avtomatik Transactional Outbox orqali e'lon qilinadi.
- Sof Multi-Tenancy modeli: `Tenant` (alohida ijarachi) va `Global` (tizim bo'yicha umumiy yoki super-admin).
- Fon vazifalari yoki umumiy hisobotlar uchun `using (tenantContext.DisableTenantFilter())` orqali faqat ijarachi filtrini vaqtincha ochish mumkin (`IsDeleted = false` soft-delete filtri esa doim faol qoladi).
- **Hot-Path Claims Caching & O(1) Permissions:** `CurrentUserHttpContext` da `UserId`, `Username`, `SessionId`, `SecurityStamp`, `ClientIp` va `CorrelationId` xususiyatlari so'rov davomida birinchi marta o'qilganda private backing-field'ga keshlanadi; takroriy `ClaimsPrincipal.Claims` chiziqli qidiruvi va `long.TryParse` string parsing 0 ga tushirilgan. Ruxsatlarni tekshirish (`HasPermission`) esa `HashSet<string>` orqali $O(1)$ vaqt murakkabligida bajariladi.

### 1.5. Kesh Arxitekturasi (HybridCache: L1 + L2)
- .NET 10 rasmiy **HybridCache** (L1 In-Memory + L2 Redis) qo'llaniladi.
- `ITenantCache` wrapperi orqali kesh kalitlari avtomatik ravishda tenant prefiksi bilan nomlanadi (`tenant:{id}:{service}:{key}`), bu tenantlararo kesh aralashuvini 100% bartaraf etadi.
- Teglar (`tags`) orqali bir vaqtning o'zida bir nechta bog'liq kesh yozuvlarini bekor qilish (`RemoveByTagAsync`) va to'g'ridan-to'g'ri qiymat yozish (`SetAsync`) qo'llab-quvvatlanadi.
- `HybridCache.GetOrCreateAsync` ichki **Lock-on-Read (Single-Flight)** mexanizmi orqali Cache Stampede (Dog-Piling) dan himoyalaydi.

### 1.6. Taqsimlangan Qulflash (Distributed Lock - RedLock & Watchdog)
- Redis asosidagi `IDistributedLock` parallel to'qnashuvlar (race condition) va bir vaqtning o'zida bir xil resursni o'zgartirishdan himoya qiladi.
- **Heartbeat/Watchdog va Polling Jitter:** Uzoq davom etadigan operatsiyalar paytida qulf muddatidan oldin tugab qolmasligi uchun `PeriodicTimer` yordamida avtomatik uzaytirish (heartbeat auto-renewal) amalga oshiriladi. Qulfni kutish siklida random jitter (`35-65ms`) qo'llaniladi, bu parallel so'rovlarning bir vaqtda Redis'ga zarba berishi (Lock Stampede) ni bartaraf etadi.

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
- **Parametrlashtirilgan Dynamic LINQ & Auto-Prepare:** Filter ifodalari closure wrapper orqali uzatilib, SQL parametri (`$1`, `$2`) sifatida generatsiya qilinadi. Bu EF Core'ning `CompiledQueryCache` ni 100% qayta ishlatishini va PostgreSQL Server-Side Auto-Prepare (`MaxAutoPrepare = 50`) bilan rejalarni serverda keshlab olishini ta'minlaydi.
- PostgreSQL `EF.Functions.ILike` / `EF.Functions.Like` hamda to'g'ridan-to'g'ri massiv `Contains` metodlaridan samarali foydalaniladi.
- Read-only so'rovlar uchun `ToReadOnlyListAsync()` va `AsNoTracking()` kengaytmalari orqali EF Core Change Tracker xotira yuklamasi sezilarli darajada qisqartiriladi.
- **Sahifalash:** `ToPagedResultAsync` sahifani bitta so'rov bilan (+1 oldindan ko'rish qatori) oladi va `HasMore` qaytaradi. `Total` uchun `COUNT(*)` faqat kerak bo'lganda ishlatiladi: oxirgi/to'liq bo'lmagan sahifada total (`offset + qatorlar`) hisoblanadi. `PagedRequest.IncludeTotal=false` (`?includeTotal=false`) COUNT'ni butunlay o'chiradi (`Total = -1`); katta tenantlarda (≈300 ming qator) bu ~12× tezroq. `ToPagedResultAsync(request, selector, ...)` DTO ustunlarini to'g'ridan-to'g'ri SQL'da proyeksiya qiladi.

### 1.10. Audit va Non-Repudiation (AuditSaveChangesInterceptor)
- Har bir `DbContext.SaveChangesAsync()` chaqirilganda `IAuditableEntity` va `ISoftDeletable` maydonlari avtomatik to'ldiriladi.
- O'zgarishlar (`OldValues`, `NewValues`, `UserId`, `ClientIP`) tranzaksiya bazaga commit bo'lgach (`SavedChangesAsync`), avtomatik tarzda **MassTransit Transactional Outbox** orqali `EntityAuditedEvent` sifatida navbatga chiqariladi; tranzaksiya xatolik bilan tugaganda `SaveChangesFailedAsync` orqali `AsyncLocal` holati to'liq tozalanadi (xotira sizishining oldi olingan).

### 1.11. Xavfsizlik, Idempotentlik va Himoya
- **HTTP Idempotency-Key Middleware:** Takroriy POST/PUT so'rovlarida javobni qayta beradi (`X-Cache: HIT-IDEMPOTENT`), kalit boshqa payload bilan qayta ishlatilsa `409 Conflict` qaytaradi (SHA256 hash; 1 MB dan katta tana buferlanmaydi, faqat uzunligi solishtiriladi), parallel dublikat uchun 500 ms gacha kutib, so'ng `409 request_in_progress` beradi. Redis bilan aloqa **aniq 2 ta atomar Lua chaqiruv (2 RTT)**: START (kesh bor bo'lsa qaytarish, aks holda qulf olish) va FINISH (javobni saqlash + qulfni bo'shatish). Ikkala kalit bir hash-tag (`{...}`) ostida, shuning uchun Redis Cluster'da ham ishlaydi. Uzoq davom etuvchi handler uchun qulf har 20 soniyada uzaytiriladi. Faqat `2xx` JSON (`< 1 MB`) javoblar 2 daqiqaga keshlanadi. (Oldingi realizatsiya har so'rovga ~10 ta Redis buyrug'i va ~5 RTT sarflardi: o'lchangan natija — kalitli POST 1 882 → ~3 200 rps.)
- **Session / Token Revocation Middleware:** L1 In-Memory (20 soniya) + L2 Redis orqali yaroqsiz qilingan sessiyalarni bir zumda aniqlab, `401 Unauthorized ("token_revoked")` qaytaradi.
- **Taqsimlangan Rate Limiting:** Gateway va WebCore qatlamlarida `PartitionedRateLimiter` orqali alohida Client IP (`gateway-limiter`: 100 req/s sliding window) yoki foydalanuvchi (`user:{userId}`) darchasi bo'yicha mustaqil himoyalanadi; bitta buzg'unchi mijozning butun servisni bloklab qo'yishi (Noisy Neighbor / DoS) to'liq bartaraf etilgan. Navbat (`QueueLimit`) o'chirilgan: limitdan oshgan so'rov darhol `429` oladi (navbatda kutish so'rovni oyna ochilguncha, 60 soniyagacha ushlab turardi). Gateway limiti `Gateway:RateLimit` bo'limida sozlanadi (`PermitLimit` standart 1000, `WindowSeconds`, `SegmentsPerWindow`, `QueueLimit`); eski qat'iy 100 req/s bitta IP uchun butun trafikni ~90 rps ga cheklab, p95 ni ~1 s ga ko'tarardi.
- **YARP Gateway SocketsHttpHandler & Zero-Copy Tuning:** YARP reverse proxy transportida `AutomaticDecompression = DecompressionMethods.None` (zero-copy proxying: siqilgan payloadni qayta ochib-yopish xarajatlarini yo'qotadi), `EnableMultipleHttp2Connections = true`, KeepAlive Ping (30s/5s) va `PooledConnectionIdleTimeout = 2min` faollashtirilgan, bu Gateway throughput (RPS) ini 30-40% ga oshiradi.
- **Resilience:** Polly v8 (`Microsoft.Extensions.Http.Resilience`) asosidagi `AddStandardResilienceHandler` orqali qayta urinish, circuit breaker va `BaseIntegrationOptions` ga bog'langan aniq `AttemptTimeout` hamda `TotalRequestTimeout` kafolati.
- **Health Checks & Monitoring:** `/health/live` (liveness), `/health/ready` (batafsil JSON formatda DB va Redis tayyorligi, Redis'ga yuborilgan real Ping kechikishi `pingMs`, va har bir resurs uchun 3s timeout bilan), hamda YARP Gateway Active Health Check bilan avtomatik failover.
- **Kestrel Server Cheklovlari:** Katta hajmdagi fayllarni MinIO ga xavfsiz yuklash uchun `MaxRequestBodySize` sukut bo'yicha 50MB ga moslashtirilgan (va `Kestrel:MaxRequestBodySize` orqali dinamik sozlanadi).
- **Siqish (Compression):** Brotli (asosiy, `Fastest`) + Gzip (fallback, `SmallestSize`) orqali tarmoq trafigini siqish. Server CPU yuklamasini tejash uchun siqish faqat matnli va strukturaviy ma'lumotlarga (`application/json`, `application/problem+json`, `application/xml`, `text/*`, `application/javascript`, `application/grpc-web`) yo'naltirilgan, pre-compressed ikkilik fayllar (rasm, PDF, video, ZIP) qayta siqilmaydi.
- **OpenTelemetry:** OTLP protokoli orqali so'rovlar zanjirini kuzatish (MassTransit, Npgsql, HttpClient, AspNetCore tracing va Runtime/Process metrics). Yuqori yuklamada collectorlar va tarmoq trafigini haddan tashqari to'lib ketishdan himoyalash uchun `OpenTelemetryOptions.SamplingRatio` (default `1.0`, ya'ni 100%, prod uchun sozlanuvchi) va `TraceIdRatioBasedSampler` orqali telemetriya hajmi boshqariladi.
- **Serilog Logging & Shovqinni Filtrlash:** `AppLoggerExtensions` orqali 0 StackTrace kompilyatsiya vaqtidagi caller ma'lumotlari muhrlanadi; shuningdek, Kubernetes va Gateway ning doimiy `/health` tekshiruvlari `LogEventLevel.Verbose` darajasiga tushirilib, millionlab keraksiz log shovqinlari (noise pollution) filtrlanadi. `Microsoft.EntityFrameworkCore` sukut bo'yicha `Warning` (har SQL buyrug'ini logga yozish 75 soniyalik yuklamada ~430 MB log berardi; yozish yo'lida ~10–14% sekinlashtirardi). `AppLoggerExtensions` o'chirilgan darajada ishlamaydi (`IsEnabled` tekshiruvi `LogContext.PushProperty` va argumentlar massivi yaratilishidan oldin turadi); Gateway'da `Yarp` `Warning`.
- **Global Exception & Client Abort Handling:** Mijoz brauzerni muddatidan oldin yopganda (`context.RequestAborted`), `GlobalExceptionMiddleware` o'lik tarmoq soketiga yozishga urinmasdan jarayonni xavfsiz yakunlaydi (`499/Silent Abort`).
- **Reverse Proxy & Forwarded Headers (Nginx / Ingress / Cloudflare):** Nginx, Cloudflare yoki Kubernetes Ingress ortida ishlaganda haqiqiy mijoz IP manzili va HTTPS sxemasini to'g'ri aniqlash uchun `UseForwardedHeaders` bilan `ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost` faollashtirilgan; ichki pod/container tarmoqlaridagi so'rovlar cheklanib qolmasligi uchun `KnownIPNetworks.Clear()` va `KnownProxies.Clear()` sozlangan. Pipeline'ning eng boshida turishi kafolatlangan.

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
