# ILlmCallPersistencePort (+ LlmCallRecord)

**Kaynak:** `Ports/Outbound/Observability/ILlmCallPersistencePort.cs`
**Implementasyon:** [`PostgresLlmCallUsageSink`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresLlmCallUsageSink.md) (prod); InMemory/test ortamında no-op

## 1. Ne İşe Yarar

Her LLM çağrısının kalıcı (veritabanı) kaydını tutan secondary port. `LlmCallRecord` tek bir
çağrının tam kaydını taşır (model, sağlayıcı, token sayıları, maliyet, süre, zaman damgası).

## 2. Hangi Amaçla Kullanılır

`TelemetryChatClient` her LLM çağrısından sonra `RecordAsync` ile kalıcı log yazar — admin
panelindeki geçmiş maliyet/kullanım raporları buradan beslenir.

## 3. Sorumlulukları

- **Üstlendiği:** Tek bir LLM çağrı kaydını fire-and-forget şekilde kalıcı depoya yazmak.
- **Üstlenmediği:** Anlık toplam sayaçlar — o [`ICostUsageStorePort`](ICostUsageStorePort.md)'un işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Postgres/PostgresLlmCallUsageSink` implemente eder; test/InMemory
ortamında no-op bir implementasyon kullanılır (log kaydı test assertion'larını etkilemesin
diye).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> **"Hata durumunda caller'a exception sızdırmaz"** — telemetri/log yazımı asla ana iş akışını
> (kullanıcıya cevap dönme) kesintiye uğratmamalıdır. Bir log satırının yazılamaması kullanıcı
> deneyimini etkilememelidir; bu yüzden implementasyon içeride hataları yutar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task RecordAsync(LlmCallRecord record, CancellationToken ct = default)` | Tek bir LLM çağrısını persist eder. |

**`LlmCallRecord(string Model, string Provider, long InputTokens, long OutputTokens, decimal CostUsd, double DurationMs, DateTime CalledAt)`** — persist edilen kaydın tüm alanları.

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
