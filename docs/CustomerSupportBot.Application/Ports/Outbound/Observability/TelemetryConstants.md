# TelemetryConstants

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/Observability/TelemetryConstants.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound.Observability`

## Ne işe yarar?

`TelemetryConstants`, <summary> OpenTelemetry ActivitySource ve Meter isimleri — tek kaynak noktası. Adapter'lar bu sabitlerden OTel kaynaklarını oluşturur. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`TelemetryConstants`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
