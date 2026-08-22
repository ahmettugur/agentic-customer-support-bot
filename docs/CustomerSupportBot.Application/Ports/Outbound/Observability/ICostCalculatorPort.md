# ICostCalculatorPort

**Kaynak:** `Ports/Outbound/Observability/ICostCalculatorPort.cs`
**Implementasyon:** [`CostCalculator`](../../../../CustomerSupportBot.Adapters.Telemetry/OpenTelemetry/CostCalculator.md)

## 1. Ne İşe Yarar

LLM kullanım maliyetini (USD) model/sağlayıcı/token sayısına göre hesaplayan secondary port.

## 2. Hangi Amaçla Kullanılır

`TelemetryChatClient` (Adapters.Telemetry) her LLM çağrısından sonra bu port ile maliyeti
hesaplar ve `ICostUsageStorePort`/`ILlmCallPersistencePort`'a kaydeder.

## 3. Sorumlulukları

- **Üstlendiği:** Model+sağlayıcı+token sayısından USD maliyeti hesaplamak, bilinen model
  listesini raporlamak.
- **Üstlenmediği:** Maliyetin biriktirilmesi/saklanması — o [`ICostUsageStorePort`](ICostUsageStorePort.md)'un işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Telemetry/OpenTelemetry/CostCalculator` implemente eder; model başına fiyat
tablosunu (input/output token başı USD) sabit kodlu tutar.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Maliyet hesaplama mantığının telemetri adaptöründe izole tutulması, fiyat tablosu güncellemesinin
(sağlayıcılar fiyat değiştirdiğinde) Application katmanına dokunmadan yapılabilmesini sağlar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `decimal CalculateCost(string modelHint, string provider, int inputTokens, int outputTokens)` | Verilen model+sağlayıcı+token sayısı için USD maliyeti hesaplar. |
| `IReadOnlyCollection<string> KnownModels { get; }` | Bilinen model adları (UI için — örn. admin dashboard'da filtre). |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
