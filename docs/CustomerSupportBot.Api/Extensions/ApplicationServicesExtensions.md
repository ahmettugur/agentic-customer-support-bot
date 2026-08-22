# ApplicationServicesExtensions

- **Dosya:** `Extensions/ApplicationServicesExtensions.cs`
- **Namespace:** `CustomerSupportBot.Api.Extensions`

## 1. Ne İşe Yarar

Application katmanı driving port'larını, CORS'u, JSON serileştirme ayarlarını, rate-limiting
politikalarını (`auth`/`chat`/`general`/`a2a`) ve arka plan servislerinin (`SlaGuardianService`,
`KnowledgeBaseStartupService`) DI kaydını tek noktada toplayan Composition Root extension'ı.

## 2. Hangi Amaçla Kullanılır

`Program.cs` açılışta `AddApplicationServices(configuration)` çağırır; bu, uygulamanın "iş
mantığı + HTTP altyapısı" tarafının büyük bölümünü tek bir çağrıyla kurar.

## 3. Sorumlulukları

- `services.AddApplicationDrivingPorts(configuration)` ile Application katmanının kendi DI
  kayıtlarını tetikler (hexagonal: API katmanı yalnızca "başlat" der, ne kaydedileceğine
  Application katmanı karar verir).
- CORS politikasını `Cors:AllowedOrigins` yapılandırmasından kurar; boşsa `AllowAnyOrigin`'e
  düşer (geliştirme kolaylığı).
- Enum'ları camelCase string olarak JSON'a yazacak şekilde `HttpJsonOptions`'ı yapılandırır.
- Dört rate-limit policy'si tanımlar: `auth` (IP bazlı, `JwtOptions.AuthRateLimitPerMinute`'tan
  okunur), `chat` (IP bazlı, 20/dk sabit), `general` (IP bazlı, 60/dk sabit), `a2a` (partner/özne
  kimliğine göre bölümlenmiş, `A2AOptions.RequestsPerMinute`'tan okunur).
- `services.AddAgentsAdapter()` ile Adapters.Agents katmanının (CustomerSupportTeam,
  ApprovalGateService) DI kaydını tetikler.
- `ChatEventOrchestrator`'ı `Scoped` olarak kaydeder (istek başına yeni instance — SSE/HTTP
  transport'a özgü olduğu için Singleton olamaz).
- `SlaGuardianService` ve `KnowledgeBaseStartupService`'i `IHostedService`/`BackgroundService`
  olarak kaydeder.
- **Üstlenmediği:** rate-limit reddi sonrası davranış dışında, hangi endpoint'in hangi policy'yi
  kullanacağı — o karar her endpoint dosyasında `.RequireRateLimiting("...")` ile ayrı ayrı
  verilir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `AddApplicationDrivingPorts` — `CustomerSupportBot.Application.DependencyInjection`.
- `AddAgentsAdapter` — `CustomerSupportBot.Adapters.Agents.DependencyInjection`.
- [ChatEventOrchestrator](../Services/ChatEventOrchestrator.md),
  [SlaGuardianService](../Workers/SlaGuardianService.md),
  [KnowledgeBaseStartupService](../Workers/KnowledgeBaseIngestor.md) — burada DI'a kaydedilen
  API katmanı bileşenleri.
- `JwtOptions.AuthRateLimitPerMinute`, `A2AOptions.RequestsPerMinute` — rate-limit eşiklerinin
  kaynağı; her istek anında `IOptions<T>`'ten okunur (yapılandırma reload'a duyarlı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **`auth` rate-limit'i öncesinde SINIRSIZDI ve bu bilinçli bir düzeltmeydi:** login/refresh/
  customer-login/customer-register uçlarında sınır olmaması, kimlik bilgisi tahmin etme
  (credential stuffing) ve kayıt spam'ini tek istemciden sınırsız denemeye açıyordu. IP bazlı
  bölümleme seçildi çünkü bu uçlarda henüz doğrulanmış bir kimlik (JWT claim) yok.
- **`a2a` politikası IP değil PARTNER kimliğine göre bölümlenir:** dış sistemler paylaşımlı
  proxy/bulut çıkışı arkasında aynı IP'yi paylaşabilir ya da IP değiştirebilir; kimlik token'dan
  gelir ve çağıran tarafından değiştirilemez — bu yüzden IP yerine token'daki kimlik kullanılır.
- **`ChatEventOrchestrator` `Scoped`, `Singleton` değil:** her SSE/HTTP isteği kendi orkestrasyon
  durumuna ihtiyaç duyar; Singleton olsaydı eşzamanlı istekler durumu paylaşırdı.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddApplicationServices(this IServiceCollection, IConfiguration)` | Yukarıdaki tüm kayıtları (driving port'lar, CORS, JSON, rate-limit policy'leri, agent adapter, orchestrator, hosted service'ler) tek çağrıda yapar. |

## 7. Bağımlılıklar

Extension metodu; constructor injection yok.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [AuthServicesExtensions](AuthServicesExtensions.md)
- [../Endpoints/ChatAndRealtime](../Endpoints/ChatAndRealtime.md)
