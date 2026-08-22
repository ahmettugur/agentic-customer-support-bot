# İnce Composition Root Extension'ları

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Extensions/PersistenceServicesExtensions.cs`
  - `CustomerSupportBot.Api/Extensions/RedisServicesExtensions.cs`
  - `CustomerSupportBot.Api/Extensions/TelemetryExtensions.cs`
  - `CustomerSupportBot.Api/Extensions/HealthCheckExtensions.cs`
  - `CustomerSupportBot.Api/Extensions/WebApplicationExtensions.cs`
  - `CustomerSupportBot.Api/Extensions/A2AServicesExtensions.cs`
- **Namespace:** `CustomerSupportBot.Api.Extensions`

## 1. Ne İşe Yarar

Altı küçük extension sınıfı, her biri kendi Adapters katmanının (Persistence, Redis, Telemetry)
`Add*Adapters(...)` çağrısına **ince bir geçiş (pass-through)** sağlar, ya da (health check,
migration, A2A hosting kaydı gibi) API katmanına özgü tek bir küçük sorumluluğu yerine getirir.
Ortak temaları: her biri tek bir `IServiceCollection`/`WebApplication` extension metodu içerir ve
gerçek iş mantığını barındırmaz.

## 2. Hangi Amaçla Kullanılır

`Program.cs` açılışında sırayla çağrılır; her biri DI konteynerine kendi katmanının servislerini
ekler veya (WebApplicationExtensions) uygulama başladıktan sonra bir kurulum adımı (migration)
çalıştırır.

## 3. Sorumlulukları ve Metotlar

### `PersistenceServicesExtensions`

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddPersistenceServices(this IServiceCollection, IConfiguration)` | `CustomerSupportBot.Adapters.Persistence.DependencyInjection.AddPersistenceAdapters`'a doğrudan delege eder. Tüm gerçek kayıt mantığı Adapters.Persistence'tedir. |

### `RedisServicesExtensions`

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddRedisServices(this IServiceCollection, IConfiguration)` | `Adapters.Redis.DependencyInjection.AddRedisAdapters`'a delege eder. |

### `TelemetryExtensions`

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddTelemetryServices(this IServiceCollection, IConfiguration)` | `Adapters.Telemetry.DependencyInjection.AddTelemetryAdapters`'a, `ActivitySource`/`Meter` adlarını (`CustomerSupportTelemetry.ActivitySourceName/MeterName`) API katmanından geçirerek delege eder. |

### `HealthCheckExtensions`

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddAppHealthChecks(this IServiceCollection, IConfiguration)` | `PersistenceOptions.Provider` Postgres ise `PostgresHealthCheck`'i, her koşulda `RedisHealthCheck`'i kaydeder — `db`/`ready` ve `cache`/`ready` etiketleriyle. |
| `static IEndpointRouteBuilder MapAppHealthChecks(this IEndpointRouteBuilder)` | Üç uç map eder: `GET /health/live` (her zaman `200 alive`, hiçbir bağımlılığı kontrol etmez), `GET /health/ready` (yalnızca `ready` etiketli kontroller — orkestratörün trafiği yönlendirmeden önce sorduğu), `GET /health` (tüm kontroller, insan tarafından okunacak tam rapor). |
| `WriteJsonResponse(HttpContext, HealthReport)` *(private)* | Health check sonucunu düz JSON'a serileştirir. |

### `WebApplicationExtensions`

| Üye | Açıklama |
|---|---|
| `static Task MigrateIfDevelopmentAsync(this WebApplication)` | Yalnızca `Development` ortamında VE sağlayıcı Postgres ise `ctx.Database.MigrateAsync()` çağırır. `ctx.Database.IsRelational()` kontrolü, test host'unun EF InMemory sağlayıcısında (migration desteklemez) açılışın patlamasını önler. |

### `A2AServicesExtensions`

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddA2AAgents(this IServiceCollection)` | Üç A2A ajanını (`Product`, `Order`, `Complaint`) MAF'ın `AddAIAgent(name, factory) + AddA2AServer(...)` hosting kaydına bağlar. Ajan örnekleri `A2AAgentCatalog`'dan (Adapters.Agents) alınır, burada yeniden kurulmaz. `AgentRunMode.DisallowBackground` ile arka plan görev çalıştırmayı engeller. |

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `AddPersistenceAdapters`, `AddRedisAdapters`, `AddTelemetryAdapters` — ilgili Adapters
  projelerindeki gerçek DI kayıt metotları.
- `PostgresHealthCheck`, `RedisHealthCheck` — Adapters.Persistence/Adapters.Redis'te tanımlı
  `IHealthCheck` implementasyonları.
- `A2AAgentCatalog` — [../../CustomerSupportBot.Adapters.Agents/A2A/A2AAgentCatalog.md](../../CustomerSupportBot.Adapters.Agents/A2A/A2AAgentCatalog.md).
- [Endpoints/A2A.md](../Endpoints/A2A.md) — `A2AServicesExtensions`'ın kaydettiği ajanları
  HTTP'ye açan taraf.
- `Program.cs` — tüm bu extension'ları sırayla çağıran Composition Root.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **Neden altısı tek dosyada belgeleniyor, kod tarafında değil:** her biri tek satırlık bir
  delegasyon ya da tek bir dar sorumluluk taşıyor; skill kuralı her class için ayrı dosya istese
  de, bu kadar ince sınıfları ayrı ayrı dosyalara bölmek okuyucuyu (stajyer) altı neredeyse boş
  sayfaya yönlendirip asıl bilgiyi (ilgili Adapters katmanındaki gerçek kayıt) gizlerdi — bu
  yüzden burada TEK dosyada, birbirine referans vererek toplanmıştır. Kod tarafında hâlâ altı
  ayrı `.cs` dosyasıdır ve o ayrım korunmuştur.
- **`/health/live` vs `/health/ready` ayrımı standart bir Kubernetes deseni:** "yaşıyor mu"
  (process çöktü mü) ile "trafik almaya hazır mı" (DB/Redis bağlantısı var mı) farklı sorulardır;
  tek bir uç bu ikisini karıştırsaydı, DB geçici olarak erişilemez olduğunda orkestratör pod'u
  YANLIŞLIKLA yeniden başlatabilirdi (oysa doğru davranış trafiği o pod'a göndermemek, pod'u
  öldürmemektir).
- **Migration yalnızca Development'ta otomatik çalışır:** production'da migration'ın ayrı,
  gözlemlenebilir bir deploy adımı olması gerekir — uygulama açılışının migration
  başarısızlığına bağlı olması (ve migration'ın hangi pod'da/ne zaman çalıştığının belirsiz
  olması) operasyonel risk taşır.

## 6. Bağımlılıklar

Hepsi extension metodu; constructor injection yok.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [ApplicationServicesExtensions](ApplicationServicesExtensions.md)
- [AiServicesExtensions](AiServicesExtensions.md)
