# CustomerSupportBot.Adapters.Telemetry

Bu klasör, hexagonal mimaride **Driven Adapter (Çıkış Adaptörü)** rolünü üstlenen; OpenTelemetry Tracing & Metrics, LLM belirteç (token) sayımı, çağrı süresi (latency) ölçümü, USD cinsinden maliyet hesaplama ([ICostCalculatorPort](../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.md)) ve canlı kullanım ambarını ([ICostUsageStorePort](../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostUsageStorePort.md)) somutlaştıran adaptördür.

## Dizin Yapısı

- [Chat/TelemetryChatClient](Chat/TelemetryChatClient.md) — `DelegatingChatClient` ile her LLM çağrısında `ai.chat` span'i açan, token/maliyet hesaplayan ve metrikleri güncelleyen MEAI sarmalayıcısı.
- [OpenTelemetry/CustomerSupportTelemetry](OpenTelemetry/CustomerSupportTelemetry.md) — Uygulama genelinde paylaşılan `ActivitySource`, `Meter`, counter ve histogram sayaçları (`ai.llm.calls`, `ai.tokens.input`, `ai.cost.usd` vb.).
- [OpenTelemetry/CostCalculator](OpenTelemetry/CostCalculator.md) — [ICostCalculatorPort](../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostCalculatorPort.md) portunu uygulayan; model bazlı USD/1K token fiyatlandırma motoru.
- [OpenTelemetry/CostUsageStore](OpenTelemetry/CostUsageStore.md) — [ICostUsageStorePort](../CustomerSupportBot.Application/Ports/Outbound/Observability/ICostUsageStorePort.md) portunu uygulayan; model bazlı bellek içi anlık maliyet, gecikme ve kullanım ambarı.
- [Options/TelemetryOptions](Options/TelemetryOptions.md) — `TelemetryOptions` strongly-typed yapılandırma modeli (gerçekte kullanılan tek yapılandırma sınıfı).
- [DependencyInjection/TelemetryAdapterServiceCollectionExtensions](DependencyInjection/TelemetryAdapterServiceCollectionExtensions.md) — `AddTelemetryAdapters` OpenTelemetry ve adaptör DI kayıt uzantısı.
- [Models/TelemetryAdapterOptions](Models/TelemetryAdapterOptions.md) — ⚠️ **ölü kod**, hiçbir yerde kullanılmıyor; `TelemetryOptions`'ın kullanılmayan öncülü/kalıntısı.

## Mimari Rolü ve Yetenekleri

- **Uçtan Uca Dağıtık İzleme (Tracing):** Her kullanıcı mesajı, alt ajan adımı (`agent.*`), araç çalıştırma (`tool.*`) ve LLM çağrısı (`ai.*`) hiyerarşik span'ler olarak izlenir.
- **Detaylı Token ve Maliyet Analizi:** Her LLM yanıtının `UsageDetails` verisinden input/output token'ları okunur; `CostCalculator` ile anlık dolar maliyeti çıkarılır.
- **OTLP Dışa Aktarım Desteği:** Tracing ve Metrics verileri gRPC veya HTTP Protobuf protokolü ile Jaeger, Prometheus, Grafana Tempo veya Azure Application Insights sistemlerine aktarılabilir.
