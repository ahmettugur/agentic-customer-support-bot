# TelemetryAdapterOptions

- **Kaynak:** `CustomerSupportBot.Adapters.Telemetry/Models/TelemetryAdapterOptions.cs`
- **Tür:** `public class`
- **Namespace:** `CustomerSupportBot.Adapters.Telemetry.Models`

## Ne işe yarar?

`TelemetryAdapterOptions`, yapısal olarak [TelemetryOptions](../Options/TelemetryOptions.md) ile neredeyse birebir aynı alanları (`Enabled`, `TracingEnabled`, `MetricsEnabled`, `ServiceName`, `ServiceVersion`, `Otlp`, `Pricing`) taşıyan bir yapılandırma modelidir.

> ⚠️ **Ölü kod — kullanılmıyor.** `grep -rn "TelemetryAdapterOptions"` sonucu bu sınıfa yalnızca kendi dosyasından referans verildiğini gösteriyor; hiçbir `.cs` dosyası bu tipi enjekte etmiyor, `IOptions<TelemetryAdapterOptions>` olarak çözmüyor veya `services.Configure<TelemetryAdapterOptions>()` ile bağlamıyor. [TelemetryAdapterServiceCollectionExtensions.cs](../DependencyInjection/TelemetryAdapterServiceCollectionExtensions.md) dosyasının başındaki yorum bunu doğruluyor: *"TelemetryOptions (Options/TelemetryOptions.cs) kullanır — TelemetryAdapterOptions kaldırıldı."* Gerçek DI ve OTLP boru hattı tamamen [TelemetryOptions](../Options/TelemetryOptions.md) üzerinden çalışıyor.

## Hangi amaçla kullanılır?

Hiçbir çalışma zamanı akışında kullanılmıyor. Muhtemelen `TelemetryOptions`'ın öncülü/ilk tasarımıydı ve `TelemetryOptions`'a geçilirken silinmesi unutulmuş bir kalıntı dosyadır.

## Sorumlulukları

- **Üstlendiği:** Hiçbir şey — hiçbir servis tarafından `IOptions<T>` olarak enjekte edilmiyor, hiçbir `IConfiguration` bölümüne bağlanmıyor.
- **Üstlenmediği:** Gerçek OpenTelemetry/maliyet yapılandırması — bu iş tamamen [TelemetryOptions](../Options/TelemetryOptions.md) tarafından karşılanıyor.

## Diğer katman ve bileşenlerle ilişkileri

Yok. Hiçbir sınıf tarafından referans alınmıyor, hiçbir DI kaydına konu değil.

## Kullanılma nedeni ve tasarım yaklaşımı

Bu dosya, projeye yeni başlayan biri için bir tuzaktır: isim benzerliği (`TelemetryAdapterOptions` vs `TelemetryOptions`) nedeniyle "hangisini kullanmalıyım?" sorusu doğurabilir. Doğru cevap her zaman [TelemetryOptions](../Options/TelemetryOptions.md)'dır. Bu sınıfın kaldırılması (silinmesi) önerilir; dokümantasyon güncelleme kapsamı dışında olduğu için burada yalnızca mevcut durumu doğru şekilde işaretlemekle yetinilmiştir.

## Metotlar / Üyeler

Salt veri taşıyan bir POCO — davranış (metot) içermez.

- `SectionName` (`const string`): `"Telemetry"` — [TelemetryOptions.SectionName](../Options/TelemetryOptions.md) ile aynı değer, kullanılmıyor.
- `Enabled` (`bool`, varsayılan `true`)
- `TracingEnabled` (`bool`, varsayılan `true`)
- `MetricsEnabled` (`bool`, varsayılan `true`)
- `ServiceName` (`string`, varsayılan `"CustomerSupportBot"`)
- `ServiceVersion` (`string`, varsayılan `"1.0.0"`)
- `Otlp` (`OtlpExporterOptions`): iç içe sınıf — `Endpoint`, `Protocol`, `Headers`.
- `Pricing` (`Dictionary<string, ModelPricing>`): iç içe sınıf — `InputPer1K`, `OutputPer1K`.

## Bağımlılıklar

Yok — dış bağımlılığı olmayan salt veri sınıfı.
