# LlmCallUsageEntity

**Dosya:** `EfCore/Entities/Observability/LlmCallUsageEntity.cs`
**Şema/Tablo:** `observability.llm_call_usage`
**Configuration:** [LlmCallUsageConfiguration](../../Configurations/Observability/LlmCallUsageConfiguration.md)

## 1. Ne İşe Yarar

Sisteme yapılan **her** LLM çağrısının maliyetini ve performansını kalıcı olarak kaydeden
varlıktır — hangi model, hangi sağlayıcı (`Provider`), kaç token, ne kadar süre, ne kadar
maliyet.

## 2. Hangi Amaçla Kullanılır

`TelemetryChatClient` (Adapters.Telemetry katmanı) her LLM çağrısından sonra bu tabloya bir
kayıt yazar (`RecordFailure`/`PersistAsync`); admin panelindeki maliyet/kullanım raporları
(`CostUsageStore`) buradan beslenir.

## 3. Sorumlulukları

- **Üstlendiği:** Tek bir LLM çağrısının ham metriklerini (token, süre, maliyet) taşımak.
- **Üstlenmediği:** Maliyet hesaplama mantığı (fiyatlandırma tablosu) — bu `CostCalculator`'ın
  (Adapters.Telemetry) işi, entity sadece hesaplanmış sonucu (`CostUsd`) saklar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız bir kayıt — belirli bir `ReasoningTraceEntity`'ye doğrudan bağlanmaz (bir trace içinde
birden fazla LLM çağrısı olabilir ama bu tablo bireysel çağrı seviyesindedir, trace seviyesinde
değil).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`CostUsd` alanı `numeric(12,8)` gibi yüksek hassasiyetli bir kolon tipiyle saklanır — LLM
çağrıları genelde çok küçük miktarlarda (ör. $0.00001234) maliyete sahiptir; standart
`numeric(10,2)` (para birimi için tipik) bu küçük değerleri sıfıra yuvarlardı, bu yüzden 8
ondalık basamak kullanılmıştır.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `long` | Birincil anahtar, otomatik artan. |
| `Model` | `string` | Kullanılan model adı (ör. "gpt-5"). |
| `Provider` | `string` | Sağlayıcı (ör. "OpenAI"). |
| `InputTokens` | `long` | Girdi token sayısı. |
| `OutputTokens` | `long` | Çıktı token sayısı. |
| `CostUsd` | `decimal` | Hesaplanmış maliyet, `numeric(12,8)`. |
| `DurationMs` | `double` | Çağrının süresi (milisaniye). |
| `CalledAt` | `DateTime` | Çağrı zamanı. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [LlmCallUsageConfiguration](../../Configurations/Observability/LlmCallUsageConfiguration.md)
- [README](../README.md)
