# Replay.razor

## Ne İşe Yarar
Reasoning trace'lerini adım adım kronolojik sırayla oynatarak AI'ın karar sürecini görselleştiren sayfadır.

## Hangi Amaçla Kullanılır
Bir trace ID'si ile açılarak, o trace'in başlangıcından sonuna kadar reasoning, tool çağrıları, agent ziyaretleri ve final response adımlarını sıralı şekilde gösterir.

## Sorumlulukları
- Trace detayını API'den çekmek.
- `TraceDetail` verisinden `ReplayStep` listesi oluşturmak (client-side dönüşüm).
- Adımları kronolojik sırada, animasyonlu şekilde oynatmak.
- Her adım tipine uygun görsel render: Init, Agent Visit, Tool Call, Reasoning, Planning, Final.
- İleri/geri navigasyon ve otomatik oynatma.

## Erişim
`[Authorize(Roles = "Admin")]` — `/traces/*` API'leri `Program.cs`'de `Admin` rolüyle
korunuyor; sayfa yetkisi API ile örtüşüyor (bkz. [Traces.md](Traces.md#erişim) — aynı
düzeltme beş admin sayfasında birlikte yapıldı).

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [TracesApiService](../Services/TracesApiService.md).
- **Model bağımlılığı**: [TraceDetailModels](../Models/TraceDetailModels.md) — `ReplayStep`, `ReplayStepPayload` hiyerarşisi.
- **İlişkili helper**: [JsonExtensions](../Helpers/JsonExtensions.md).

## Bağımlılıklar
- [TracesApiService](../Services/TracesApiService.md), [TraceDetailModels](../Models/TraceDetailModels.md).
