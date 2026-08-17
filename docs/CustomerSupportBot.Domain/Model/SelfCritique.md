# SelfCritique

**Dosya:** `Model/SelfCritique.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

`ResponseAgent`'ın kendi yanıtı hakkında ürettiği **kalite değerlendirmesi**dir. Yanıtın kullanıcının sorusunu karşılayıp karşılamadığı, ton, tamlık, hallucination riski gibi metrikleri içerir.

## 2. Hangi Amaçla Kullanılır

ResponseAgent prompt'unda yanıt üretildikten **sonra** istenir — yanıtı düzeltmek için değil, ölçmek için vardır. Kullanıcıya **asla gösterilmez** (`WorkflowResponseExtractor.RemoveTechnicalJsonBlocks` temizler). Trace'e yazılır ve `LessonMiner`'ın "hangi turlar incelenmeli" seçiminde sinyal olarak kullanılır.

> 💡 **Analiz notu:** Öğretmenin kendi ödevini notlaması gibi düşün — "Bu yanıtım tam mı? Hallucination var mı? Ton uygun mu?" diye kendini değerlendirir. Ama öğrenci (kullanıcı) bu notu görmez.

## 3. Sorumlulukları

- ✅ Yanıt kalitesini çok boyutlu olarak ölçmek
- ✅ `IsConcerning` property'si ile potansiyel sorunları otomatik bayraklamak
- ❌ Yanıtı düzeltmek — sadece ölçer
- ❌ Kullanıcıya gösterilmek — workflow tarafından temizlenir

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `SelfCritiqueParser.Parse()` — ResponseAgent çıktısındaki JSON bloğunu parse eder
- **Kim tüketir:**
  - `WorkflowTraceEventProcessor` — trace'e yazar
  - `LessonMiner` — `IsConcerning` true ise inceleme adayı olarak seçer
  - `WorkflowResponseExtractor` — JSON bloğunu kullanıcı yanıtından temizler

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **Neden çalışma zamanı kararlarını etkilemiyor?** Tüm alanlar modelin beyanıdır — doğrulanmış ölçüm değildir. Model "hallucination riskim düşük" dese bile gerçekten öyle olmayabilir. Bu yüzden yalnızca gözlemlenebilirlik ve iyileştirme adayı seçimi için kullanılır.

> 💡 **`IsConcerning` computed property:** Modelin kendi `RevisionNeeded` flag'ini koymayı unutması durumunda bile aynı sonuca varılır — prompt'taki kurallarla bilinçli olarak aynı eşikler kullanılır (hallucinationRisk ≥ 0.5, completeness < 0.7, tone ∈ {robotic, impolite}).

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `AddressesUserQuery` | `bool` | Kullanıcının asıl sorusu yanıtlandı mı? |
| `Tone` | `string` | Ton: "appropriate", "too_formal", "too_casual", "robotic", "impolite" |
| `Completeness` | `double` | 0.0–1.0 — tamlık (1.0 = tam yanıt) |
| `HallucinationRisk` | `double` | 0.0–1.0 — specialist çıktısında olmayan veri üretme riski |
| `Sources` | `List<string>` | Yanıtın beslendiği kaynaklar (ör. "OrderAgent.resultNotes") |
| `IssuesFound` | `List<string>` | Modelin tespit ettiği sorunlar |
| `RevisionNeeded` | `bool` | Model düzeltme gerektiğini düşünüyor mu? |
| `RevisionNotes` | `string?` | Düzeltme önerisi |
| `IsConcerning` | `bool` | **Computed** — kalite endişesi var mı? |

### IsConcerning Tetikleme Koşulları

- `RevisionNeeded == true`
- `AddressesUserQuery == false`
- `HallucinationRisk >= 0.5`
- `Completeness < 0.7`
- `Tone == "robotic"` veya `Tone == "impolite"`

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [../../CustomerSupportBot.Adapters.Agents/Team/ResponseAgent.md](../../CustomerSupportBot.Adapters.Agents/Team/ResponseAgent.md) — Bu değerlendirmeyi üreten ajan
- [../Services/SelfCritiqueParser.md](../Services/SelfCritiqueParser.md) — JSON → SelfCritique dönüşümü
- [../../CustomerSupportBot.Application/LessonMiner.md](../../CustomerSupportBot.Application/Improvement/LessonMiner.md) — İnceleme adayı seçimi
