# ICostUsageStorePort (+ CostModelUsageSnapshot, CostUsageSnapshot)

**Kaynak:** `Ports/Outbound/Observability/ICostUsageStorePort.cs`
**Implementasyon:** [`CostUsageStore`](../../../../CustomerSupportBot.Adapters.Telemetry/OpenTelemetry/CostUsageStore.md)

## 1. Ne İşe Yarar

Toplam token/maliyet sayaçlarını tutan ve okuyan secondary port. `CostUsageSnapshot`
uygulama genelindeki toplamı, `CostModelUsageSnapshot` model bazlı kırılımı temsil eder.

## 2. Hangi Amaçla Kullanılır

Admin dashboard'daki maliyet/kullanım paneli `GetUsageSnapshot()` ile anlık durumu çeker;
`ResetUsage()` sayaçları sıfırlar (örn. faturalama dönemi başında).

## 3. Sorumlulukları

- **Üstlendiği:** Süreç içinde biriken sayaçları okuma/sıfırlama.
- **Üstlenmediği:** Maliyet hesaplama ([`ICostCalculatorPort`](ICostCalculatorPort.md)'ın işi),
  kalıcı (veritabanı) kayıt ([`ILlmCallPersistencePort`](ILlmCallPersistencePort.md)'ın işi —
  bu port yalnızca **in-memory** toplam sayaçtır, pod yeniden başlarsa sıfırlanır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Telemetry/OpenTelemetry/CostUsageStore` implemente eder; `TelemetryChatClient` her
çağrıdan sonra buraya yazar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

In-memory bir sayaç olmasının nedeni hız — dashboard'ın her yenilenişinde veritabanına gitmesi
gerekmez. Kalıcı geçmiş gerektiğinde `ILlmCallPersistencePort` üzerinden Postgres'e yazılan
kayıtlar kullanılır; bu iki port'un ayrı olması "hızlı anlık görünüm" ile "kalıcı audit
kaydı" sorumluluklarını birbirinden ayırır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `CostUsageSnapshot GetUsageSnapshot()` | Toplam ve model bazlı kullanım özetini döner. |
| `void ResetUsage()` | Tüm sayaçları sıfırlar. |

**`CostModelUsageSnapshot(string Model, long Calls, long InputTokens, long OutputTokens, decimal CostUsd, double AverageLatencyMs, DateTime LastUsed)`** — tek bir modelin kullanım özeti.

**`CostUsageSnapshot(long TotalCalls, long TotalInputTokens, long TotalOutputTokens, decimal TotalCostUsd, IReadOnlyList<CostModelUsageSnapshot> ByModel)`** — uygulama genelindeki toplam + model bazlı kırılım.

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
