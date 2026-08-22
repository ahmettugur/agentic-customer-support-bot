# Program (Composition Root)

- **Kaynak:** `CustomerSupportBot.Api/Program.cs`
- **Tür:** `Top-Level Entry Point`
- **Namespace:** `CustomerSupportBot.Api`

## Ne işe yarar?

`Program.cs`, tüm projenin başlangıç noktası (Entry Point) ve Hexagonal Mimarinin montaj köküdür (**Composition Root**). Tüm adaptör katmanlarını (`Domain`, `Application`, `Adapters.AI`, `Adapters.Agents`, `Adapters.Persistence`, `Adapters.Redis`, `Adapters.Telemetry`), güvenlik middleware'lerini ve Minimal API uç noktalarını bir araya getirerek ASP.NET Core `WebApplication`'ı inşa eder ve başlatır.

## Hangi amaçla kullanılır`?

- `builder.Services` üzerinden tüm bağımlılıkları (`AddPersistenceAdapters`, `AddRedisAdapters`, `AddAiAdapters`, `AddAgentAdapters`, `AddApplicationServices`, `AddTelemetryAdapters`, `AddAuthServices`) doğru sırada kaydetmek.
- HTTP istek hattını (`UseExceptionHandler`, `UseCors`, `UseAuthentication`, `UseAuthorization`, `UseWebSockets`) yapılandırmak.
- Tüm `Map*Endpoints` fonksiyonlarını çağırarak API rotalarını bağlamak.

## Başlatma Sırası ve Mantığı

1. **Host ve Konfigürasyon:** `WebApplication.CreateBuilder(args)` oluşturulur.
2. **Kalıcılık ve Altyapı:** PostgreSQL EF Core DbContext, Redis bağlantısı ve OTLP Telemetri boru hattı kaydedilir.
3. **AI ve Ajanlar:** OpenAI/Azure istemcileri, Qdrant vektör ambarı ve MAF ajan takımı (`AddAgentAdapters`) bağlanır.
4. **Güvenlik ve Kimlik:** JWT Authentication (`AddJwtBearer`), Rol tabanlı Authorization ve CORS politikaları eklenir.
5. **Middleware Hattı:** `UseExceptionHandler()`, `UseCors("CorsPolicy")`, `UseAuthentication()`, `UseAuthorization()`, `UseWebSockets()` sırayla eklenir.
6. **Uç Nokta Haritalama:** `app.MapChatEndpoints()`, `app.MapRealtimeEndpoints()`, `app.MapAdminEndpoints()`, `app.MapTraceEndpoints()` vb. rotalanır.
7. **Veritabanı Tohumlama:** `DemoDataSeeder.SeedAsync` çağrılarak eksik demo verileri oluşturulur.
8. **Sunucunun Başlatılması:** `app.Run()` ile HTTP/WebSocket dinleyicisi başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Application`
- `CustomerSupportBot.Adapters.AI`
- `CustomerSupportBot.Adapters.Agents`
- `CustomerSupportBot.Adapters.Persistence`
- `CustomerSupportBot.Adapters.Redis`
- `CustomerSupportBot.Adapters.Telemetry`
