# AiServicesExtensions

- **Dosya:** `Extensions/AiServicesExtensions.cs`
- **Namespace:** `CustomerSupportBot.Api.Extensions`

## 1. Ne İşe Yarar

`Adapters.AI` (LLM istemci oluşturma) ve `Adapters.Telemetry` (maliyet/kullanım dekorasyonu)
katmanlarının kesişimini kuran Composition Root extension'ı: standart ve reasoning `IChatClient`
örneklerini oluşturur, telemetri açıksa bunları `TelemetryChatClient` ile sarar.

## 2. Hangi Amaçla Kullanılır

`Program.cs`'te `AddAiServices(configuration)` ile çağrılır; sonuçta DI konteynerine
`IChatClient`, `ReasoningChatClient`/`IReasoningChatClient`, `IGeneralChatClient` kayıtlı olur —
bunlar Agents/Application katmanlarındaki her ajan ve reasoning bileşeni tarafından tüketilir.

## 3. Sorumlulukları

- `AddAiAdapters(configuration)` ile temel AI adapter kayıtlarını tetikler.
- Standart `IChatClient`'ı `AiClientFactory.CreateStandardChatClient` ile oluşturur, telemetri
  açıksa sarar.
- `ReasoningChatClient`'ı (reasoning-özel model, ör. o1/o3 sınıfı) ayrı bir factory ile oluşturur.
- `IGeneralChatClient`'ı (`GeneralChatClientAdapter`) `IChatClient` üzerine ince bir sarmalayıcı
  olarak kaydeder.
- **Üstlenmediği:** LLM sağlayıcısına (OpenAI/Azure OpenAI) özgü HTTP/SDK detayları —
  `AiClientFactory` (Adapters.AI) içindedir; maliyet hesaplama mantığı — `ICostCalculatorPort`
  implementasyonundadır.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `AiClientFactory`, `IGeneralChatClient`, `GeneralChatClientAdapter` — `Adapters.AI`.
- `TelemetryChatClient`, `CostUsageStore`, `ICostCalculatorPort` — `Adapters.Telemetry`.
- `AiOptions`, `TelemetryOptions` — sırasıyla sağlayıcı/model seçimi ve telemetri açma/kapama
  yapılandırması.
- Bu dosyanın ürettiği `IChatClient`, Adapters.Agents katmanındaki `CustomerSupportTeam`
  (workflow ajanları) tarafından kullanılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **AI client oluşturma ile telemetri dekorasyonunun BİRLEŞTİRİLMESİ bilinçlidir** (dosya başı
  yorumunda da belirtilmiş): iki adapter'ın (`Adapters.AI`, `Adapters.Telemetry`) kesişimi olan
  bu "hangi client'ı hangi dekoratörle sarmalıyorum" kararı, tanım gereği Composition Root'un
  işidir — ne AI adapter'ın telemetriyi bilmesi ne de telemetri adapter'ın AI oluşturmayı
  bilmesi doğru olurdu.
- **Standart ve reasoning client'lar AYRI kayıtlar:** bazı akışlar (basit sınıflandırma) ucuz/
  hızlı bir modelle yeterliyken, derin akıl yürütme gerektiren akışlar reasoning modeline
  ihtiyaç duyar — model seçimi çağıran koda değil, burada merkezi olarak yapılandırmaya
  bırakılmıştır.
- **Telemetri kapalıysa `WrapWithTelemetry` orijinal client'ı olduğu gibi döner:** dekoratör
  zorunlu değil, opsiyonel bir katman — telemetri kapalıyken gereksiz bir sarmalama katmanı
  performansa/karmaşıklığa katkı sağlamaz.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddAiServices(this IServiceCollection, IConfiguration)` | Tüm `IChatClient` türevlerini oluşturup DI'a kaydeder. |
| `WrapWithTelemetry(IServiceProvider, IChatClient inner, string modelHint, string provider)` *(private)* | Telemetri açıksa `inner`'ı `TelemetryChatClient` ile sarar, kapalıysa `inner`'ı olduğu gibi döner. |
| `ResolveStandardModel(AiOptions)` *(private)* | Sağlayıcıya göre (Azure/OpenAI) standart model adını çözer. |
| `ResolveReasoningModel(AiOptions)` *(private)* | Sağlayıcıya göre reasoning model adını çözer; tanımlı değilse standart modele düşer. |

## 7. Bağımlılıklar

Extension metodu; constructor injection yok — servisler `IServiceProvider` üzerinden factory
lambda'ları içinde çözülür.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [../../CustomerSupportBot.Adapters.Telemetry/README.md](../../CustomerSupportBot.Adapters.Telemetry/README.md)
