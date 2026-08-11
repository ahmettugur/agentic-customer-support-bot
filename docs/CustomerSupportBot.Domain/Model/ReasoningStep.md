# ReasoningStep

**Dosya:** `Model/ReasoningStep.cs`  
**Tür:** `class` (mutable)

## 1. Ne İşe Yarar

Reasoning sürecindeki **tek bir yapılandırılmış adımı** temsil eder. LLM'in plan satırlarını düz string yerine "aksiyon + dayanak + güven" olarak yakalar.

## 2. Hangi Amaçla Kullanılır

`ReasoningResult.Steps` listesinin her elemanıdır. UI'daki "Düşünce süreci" panelinde her adım ayrı ayrı görüntülenir. `ReasoningSanityChecker` bu adımları inceleyerek tutarsızlıkları bulur.

> 💡 **Analiz notu:** Bir doktorun muayene notlarının her satırı gibi düşün: "1. Ateş var mı? ✓ (kanıt: hasta beyanı, güven: yüksek)" — her adımda ne yapıldığı, neye dayanıldığı ve güven seviyesi açıkça belirtilir.

## 3. Sorumlulukları

- ✅ Tek bir reasoning adımının tüm boyutlarını (aksiyon, dayanak, güven, alternatif) taşımak
- ✅ Trace'de ve UI'da bağımsız olarak denetlenebilir olmak
- ❌ Adımı yürütmek — sadece veri taşır

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `ReasoningResultParser` — LLM JSON çıktısından parse eder
- **Kim tüketir:** `ReasoningSanityChecker`, UI Traces paneli

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 Eski tasarımda adımlar düz `List<string>` idi — sadece metin. Yeni tasarımda her adım yapılandırılmış çünkü:
>
> 1. **Grounding** alanı sayesinde modelin neye dayanarak karar verdiği görülebilir (debug için kritik)
> 2. **Confidence** alanı sayesinde her adımın güvenilirliği ayrı ayrı ölçülebilir
> 3. **AlternativeRejected** alanı sayesinde reddedilen alternatifler kaydedilir (transparency)
>
> LLM eski formatta (string listesi) dönerse parser otomatik olarak `Description` alanına wrap eder — geriye dönük uyumlu.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Order` | `int` | 1-indexed adım sırası (UI'da göstermek için) |
| `Description` | `string` | İnsanlara yönelik kısa açıklama (UI'da bu gösterilir) — **zorunlu** |
| `Action` | `string?` | Makine okuyabilir aksiyon etiketi: "extract", "route", "clarify", "call_tool", "verify", "terminate" |
| `Premise` | `string?` | Bu adımın geçerli olması için gereken önkoşul |
| `Grounding` | `string?` | Kanıt kaynağı: "regex", "session_state", "history", "DB", "derived", "assumption" |
| `Confidence` | `double?` | Bu adım için bağımsız güven skoru (0.0-1.0) |
| `AlternativeRejected` | `string?` | Aynı konumda düşünülüp reddedilen alternatif |

> ⚠️ `Grounding` null ise model bağımsız varsayım yapmış demektir — `ReasoningSanityChecker` bunu kırmızı bayrak olarak işaretleyebilir.

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — Bu adımları taşıyan ana model
- [ReasoningIssue.md](ReasoningIssue.md) — Adımlardaki tutarsızlıklar
