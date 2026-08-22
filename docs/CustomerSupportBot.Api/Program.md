# Program (Composition Root)

- **Kaynak:** `CustomerSupportBot.Api/Program.cs`
- **Tür:** Top-Level Entry Point (dosya kapsamında ifadeler, `Main` yok)
- **Namespace:** `CustomerSupportBot.Api` (dosya sonundaki `partial class Program` yalnızca
  `WebApplicationFactory<Program>` ile entegrasyon testlerinin uygulamayı başlatabilmesi için)

> 🐞 Bu doküman daha önce genel/şematik bir özetti ve gerçek koddan sapmıştı (ör. olmayan
> `AddAgentAdapters`/`AddAuthServices` metot adları, eksik A2A/rate-limit/guard akışı). Aşağıdaki
> içerik `Program.cs`'in güncel hâlinden satır satır çıkarıldı.

## 1. Ne İşe Yarar

Uygulamanın giriş noktası ve Hexagonal Mimarinin **montaj kökü** (Composition Root). Tüm
katmanların (`Application`, `Adapters.*`) DI kayıtlarını doğru sırayla tetikler, HTTP middleware
zincirini kurar, tüm `Map*Endpoints()` çağrılarıyla route'ları bağlar ve açılış-zamanı güvenlik
denetimlerini (guard) çalıştırır.

## 2. Hangi Amaçla Kullanılır

`dotnet run`/konteyner başlatıldığında ilk çalışan koddur. Aşağıdaki üç ana işi sırayla yapar:
servis kaydı → açılış guard'ları → middleware + endpoint haritalama → `app.Run()`.

## 3. Sorumlulukları

### 3.1 Servis Kaydı (DI)

Sıra önemlidir çünkü bazı extension'lar önceki kayıtlara bağımlıdır:

```
AddOpenApi, AddLogging, AddProblemDetails, AddExceptionHandler<DomainExceptionHandler>
AddTelemetryServices → AddAiServices → AddRedisServices → AddPersistenceServices
AddApplicationServices → AddAuthenticationServices → AddAppHealthChecks
(A2A:Enabled ise) AddA2AAgents
```

### 3.2 Açılış Guard'ları (`app.Build()` sonrası, `app.Run()` öncesi)

Üç kritik güvenlik kontrolü — hepsi yalnızca `Development` DIŞINDA çalışır ve başarısızlıkta
**uygulamayı başlatmaz** (`throw`), sessizce devam etmez:

| Guard | Kontrol | Neden başlatma hatası (uyarı değil) |
|---|---|---|
| JWT signing key | `Jwt:SigningKey` depoda bilinen bir yer tutucu içeriyor mu (`DEV_ONLY`, `REPLACE_IN_PRODUCTION`, ...) | Bu anahtarla üretimde token doğrulamak, tüm yetkilendirme zincirini (Admin/Agent/Customer) geçersiz kılar. |
| HITL | `ApprovalOptions.Enabled=false` ise | Uyarı olarak loglanır (throw değil) — bilinçli bir tercih olabilir, ama operatöre açıkça bildirilir. |
| A2A `PublicBaseUrl` | Boşsa/göreli ise/HTTPS değilse (Development dışında) | A2A kartları dış istemcilere adres ilan eder; göreli/http adres, dışarıdan çözülemeyen ya da düz metin token taşıyan bir kanal yayınlamak anlamına gelir. |

### 3.3 Middleware Zinciri (SIRA KRİTİK)

```
UseExceptionHandler()
MapAppHealthChecks()
UseCors()  →  UseWebSockets()  →  (Development) MapOpenApi()
UseHttpsRedirection()
(A2A açıksa) UseA2ARejectionLogging()   ← UseAuthentication'dan ÖNCE
UseAuthentication()
UseRateLimiter()                        ← UseAuthentication'dan SONRA (bkz. 5. bölüm)
UseAuthorization()
(A2A açıksa) UseA2AProtocolGuards()      ← UseAuthorization'dan SONRA, model binding'den ÖNCE
```

### 3.4 Endpoint Haritalama ve Yetki Kapsamları

```
MapAuthEndpoints()
(A2A açıksa) MapA2AAuthEndpoints(), MapA2AAgentEndpoints()
MapChatEndpoints(), MapRealtimeEndpoints(), MapSessionEndpoints()

adminScope  = MapGroup("").RequireAuthorization("Admin").RequireRateLimiting("general")
  → MapAdminEndpoints, MapTraceEndpoints, MapEvaluationEndpoints, MapMemoryEndpoints,
    MapImprovementsEndpoints, MapTelemetryEndpoints, MapPersonalizationEndpoints,
    MapAgentsEndpoints, MapSlaEndpoints

agentScope  = MapGroup("").RequireAuthorization("AdminOrAgent").RequireRateLimiting("general")
  → MapAgentPanelEndpoints

MapAnalyticsEndpoints()   ← kendi içinde uç bazlı politika taşır (bazıları Admin, rating public)
```

**Üstlenmediği:** hiçbir iş mantığı — her `Map*Endpoints`/`Add*Services` çağrısı ilgili
katmana/dosyaya delege eder; `Program.cs` yalnızca SIRAYI ve KOŞULLARI (A2A açık mı, ortam ne)
belirler.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bu dosya, `docs/CustomerSupportBot.Api/` altındaki hemen hemen tüm dosyaların "nereden
çağrıldığı" sorusunun cevabıdır — bkz. [README.md](README.md) tam dizin için. Özellikle:

- Tüm `Extensions/*.cs` — [AuthServicesExtensions](Extensions/AuthServicesExtensions.md),
  [ApplicationServicesExtensions](Extensions/ApplicationServicesExtensions.md),
  [AiServicesExtensions](Extensions/AiServicesExtensions.md),
  [ince extension'lar](Extensions/ThinCompositionRootExtensions.md).
- Tüm `Endpoints/*.cs` — [ChatAndRealtime](Endpoints/ChatAndRealtime.md),
  [AdminAndHitl](Endpoints/AdminAndHitl.md), [A2A](Endpoints/A2A.md),
  [ObservabilityAndTelemetry](Endpoints/ObservabilityAndTelemetry.md),
  [Intelligence](Endpoints/Intelligence.md).
- [DomainExceptionHandler](Infrastructure/DomainExceptionHandler.md) — `UseExceptionHandler()`'ın
  arkasındaki implementasyon.
- `WebApplicationExtensions.MigrateIfDevelopmentAsync` — bkz.
  [ThinCompositionRootExtensions](Extensions/ThinCompositionRootExtensions.md).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **`UseA2ARejectionLogging()` `UseAuthentication()`'dan ÖNCE, `UseAuthorization()`'dan SONRA
  değil:** kod yorumunda açıkça ölçülmüş bir bulgu var — `UseAuthorization` başarısız bir
  politika karşısında kısa devre yapar ve kendisinden SONRA kayıtlı hiçbir middleware çalışmaz;
  reddedilme logu bu sıralamada tutulmasaydı, en çok görülmesi gereken olay (yetkisiz erişim
  denemesi) loglarda hiç görünmezdi.
- **`UseRateLimiter()` `UseAuthentication()`'dan SONRA çalışmak ZORUNDA:** A2A rate-limit
  politikası bölümleme anahtarını token claim'lerinden (partner kimliği) çıkarır;
  `UseAuthentication`'dan önce çalışsaydı `HttpContext.User` henüz boş olurdu, claim
  bulunamazdı ve politika sessizce IP bazlı sınırlamaya düşerdi — "partner başına limit" kuralı
  fiilen "IP başına limit" olarak çalışırdı (ölçülerek doğrulanmış bir bulgu, kod yorumunda
  açıkça belgeli).
- **`UseA2AProtocolGuards()` `UseAuthorization()`'dan SONRA, model binding'den ÖNCE:** endpoint
  metadata'sı routing sırasında hazırdır; guard yetkilendirmeden sonra çalışırsa yetkisiz
  gövdeler boşuna okunmaz (DoS yüzeyini azaltır), ama Minimal API model binding'inden önce
  çalışmalı ki yetkili A2A gövdeleri deserialize edilmeden önce boyut sınırına tabi olsun.
- **A2A tamamen `A2A:Enabled` bayrağına bağlı, sadece endpoint'ler değil DI kaydı da koşullu:**
  "kapalı bir kanalın hiç yayında olmaması, yetkiyle engellenmesinden daha güvenlidir" — yanlış
  yapılandırma (unutulmuş bir policy) burada hiçbir yüzey bırakmaz çünkü route'lar hiç
  map edilmemiştir.
- **Admin/agent uçları için `general` rate-limit sonradan eklendi:** yorumda belirtildiği gibi,
  bu uçlar auth arkasında olduğu için önceden rate limit yoktu — bir kimlik bilgisi
  sızarsa/kötüye kullanılırsa sınırsız istek atılabiliyordu; aynı eşik (`AnalyticsEndpoints`'te
  zaten kullanılan `general`, 60/dk/IP) burada da uygulandı.
- **`adminScope`/`agentScope` `MapGroup("")` ile boş prefix kullanır:** yalnızca ortak
  `RequireAuthorization`/`RequireRateLimiting`'i birden fazla `Map*Endpoints` çağrısına
  uygulamak için bir grup oluşturur; route prefix'i eklemez (her endpoint kendi tam yolunu
  tanımlar).

## 6. Metotlar / Üyeler

Dosya kapsamında (top-level statements) çalışan bir betiktir, metot/sınıf tanımlamaz — tek
istisna, dosya sonundaki `public partial class Program { }` bildirimi (entry point'i
`WebApplicationFactory<Program>` için erişilebilir kılmak amacıyla).

## 7. Bağımlılıklar

`Program.cs` doğrudan şu projelere bağımlıdır (proje referansları üzerinden): `Application`,
`Adapters.AI`, `Adapters.Agents`, `Adapters.Persistence`, `Adapters.Redis`, `Adapters.Telemetry`.

## Bağlantılar

- [README.md](README.md) — Katman indeksi (tam dosya listesi)
- [Extensions/AuthServicesExtensions](Extensions/AuthServicesExtensions.md)
- [Endpoints/A2A](Endpoints/A2A.md)
