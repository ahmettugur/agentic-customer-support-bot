# ReasoningIssue

**Dosya:** `Model/ReasoningIssue.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `IssueSeverity` enum (aynı dosyada)

## 1. Ne İşe Yarar

`ReasoningSanityChecker`'ın LLM reasoning çıktısında bulduğu **mantık tutarsızlıklarını** temsil eder. Hiçbir LLM çağrısı yapılmadan, tamamen deterministik kurallarla tespit edilir.

## 2. Hangi Amaçla Kullanılır

`ReasoningResult.SanityIssues` listesinin elemanıdır. Frontend debug panelinde görüntülenir, trace'e yazılır. `Severity=Error` olan bir issue varsa workflow güven skorunu düşürebilir veya trace'e warning ekleyebilir.

> 💡 **Analiz notu:** LLM bazen mantıksız sonuçlar üretebilir — örneğin "yüksek güvenim var ama kullanıcıdan açıklama iste" gibi çelişkili bir sonuç. Sanity checker bu tür tutarsızlıkları yakalar ve bu class ile raporlar.

## 3. Sorumlulukları

- ✅ Tek bir tutarsızlığı (kod, önem, mesaj, alan, öneri) taşımak
- ❌ Tutarsızlığı tespit etmek — bu `ReasoningSanityChecker`'ın işi
- ❌ Tutarsızlığı düzeltmek — sadece raporlar

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `ReasoningSanityChecker` (Application katmanı)
- **Kim tüketir:** UI Traces paneli, `ReasoningResult.SanityIssues`

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 `Code` alanı snake_case formatında makine okuyabilir çünkü ileride bu kodlar otomatik düzeltme kurallarına bağlanabilir. `Message` alanı Türkçe ve insanlara yönelik çünkü debug panelinde doğrudan görüntülenir.

## 6. Metotlar / Üyeler

### ReasoningIssue

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Code` | `string` | Makine okuyabilir kod (ör. "overconfident_clarification", "redundant_required_info") |
| `Severity` | `IssueSeverity` | Önem seviyesi (Info / Warn / Error) |
| `Message` | `string` | İnsanlara yönelik Türkçe açıklama |
| `Field` | `string?` | Hangi alanı işaret ediyor (ör. "nextAction", "requiredInfo[0]") |
| `SuggestedFix` | `string?` | Düzeltme önerisi (opsiyonel) |

### IssueSeverity Enum

| Değer | Açıklama |
| ------- | ---------- |
| `Info` | Bilgilendirme — davranış değiştirmeyebilir |
| `Warn` | Uyarı — kalite düşer ama bozmaz |
| `Error` | Hata — ping-pong veya hallucination riski yüksek |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — Bu issue'ları taşıyan ana model
- [ReasoningStep.md](ReasoningStep.md) — Issue'lar bu adımlarla ilişkili olabilir
