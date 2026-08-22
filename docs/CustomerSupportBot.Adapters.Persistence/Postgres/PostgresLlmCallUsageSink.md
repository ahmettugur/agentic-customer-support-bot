# PostgresLlmCallUsageSink

**Dosya:** `Postgres/PostgresLlmCallUsageSink.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`ILlmCallPersistencePort`](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ILlmCallPersistencePort.md)

## 1. Ne İşe Yarar

Her LLM çağrısının (model, sağlayıcı, token sayıları, gecikme, tahmini maliyet) bir satırını `observability.llm_call_usages` tablosuna yazan basit, cache'siz bir sink.

## 2. Hangi Amaçla Kullanılır

Telemetri katmanının (`TelemetryChatClient`) her tamamlanan LLM çağrısı sonrası çağırdığı kalıcılık noktası — maliyet/kullanım panolarının veri kaynağı.

## 3. Sorumlulukları

- Üstlendiği: `LlmCallRecord`'u satır olarak yazmak.
- Üstlenmediği: maliyet hesaplama (bu `CostCalculator`'ın işi — Adapters.Telemetry), okuma/raporlama (ayrı sorgu uçları).

## 4. İlişkiler

- `ILlmCallPersistencePort` portunu implemente eder.
- `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.

## 5. Tasarım Yaklaşımı

Diğer Postgres adaptörlerinin aksine cache/Redis/hydration YOKTUR — bu bilinçli bir sadelik: kayıt yalnızca yazılır, process-içi anlık okunmaz (raporlama ayrı, doğrudan DB sorgularıyla yapılır), bu yüzden cache karmaşıklığına gerek yoktur.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task RecordAsync(LlmCallRecord record, CancellationToken ct)` | Tek satır INSERT. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`

## Bağlantılar

- [ILlmCallPersistencePort](../../CustomerSupportBot.Application/Ports/Outbound/Observability/ILlmCallPersistencePort.md)
- [CostCalculator](../../CustomerSupportBot.Adapters.Telemetry/CostCalculator.md)
