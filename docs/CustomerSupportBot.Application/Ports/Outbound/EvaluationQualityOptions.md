# EvaluationQualityOptions

**Kaynak:** `Ports/Outbound/EvaluationQualityOptions.cs`
**Ayar bölümü:** `appsettings.json` → `"EvaluationQuality"`

## 1. Ne İşe Yarar

MEAI (`Microsoft.Extensions.AI.Evaluation.Quality`) LLM-judge kalite kontrollerinin
(relevance/coherence) global açma/kapama anahtarı.

## 2. Hangi Amaçla Kullanılır

Evaluation senaryolarındaki `quality_checks` çalıştırılırken evaluation servisi bu bayrağı
kontrol eder.

## 3. Sorumlulukları

- **Üstlendiği:** Yalnızca global bir açık/kapalı anahtarı taşımak.
- **Üstlenmediği:** Kalite değerlendirmesinin kendisi — bu, evaluation servisindeki MEAI
  entegrasyonunun işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`IEvaluationPort` implementasyonları (Application/Ports/Inbound tarafında tanımlı) bu options'ı
okur; senaryo bunları istese bile `Enabled=false` olduğu sürece atlanır
(`Skipped="quality_checks_disabled"`).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Varsayılan `false`: her koşum gerçek bir ek LLM çağrısı (judge modeli) gerektirdiği için
maliyetlidir. CI'da ayrı, isteğe bağlı bir job'da `true` yapılması önerilir — böylece her PR'da
gereksiz LLM maliyeti oluşmaz, yalnızca kalite regresyonuna özel bakılmak istendiğinde açılır.

## 6. Metotlar / Üyeler

| Üye | Varsayılan | Açıklama |
|---|---|---|
| `const string SectionName` | `"EvaluationQuality"` | appsettings bölüm adı. |
| `bool Enabled` | `false` | LLM-judge kalite kontrolleri açık mı. |

## 7. Bağımlılıklar

Yok — saf options sınıfı.
