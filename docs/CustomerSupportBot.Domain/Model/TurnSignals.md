# TurnSignals

**Dosya:** `Model/TurnSignals.cs`  
**Tür:** `sealed record` (immutable)

## 1. Ne İşe Yarar

Bir konuşma turunda LLM reasoning'inin ürettiği ve session state'e işlenecek **sinyalleri** taşıyan taşıyıcı nesnedir. Intent, duygu etiketi ve duygu skoru bilgilerini içerir.

## 2. Hangi Amaçla Kullanılır

`ChatPortService` reasoning tamamlandıktan sonra `TurnSignals.From(reasoningResult)` çağrısıyla sinyalleri çıkarır, ardından `SessionStateService.PersistExchangeAsync()`'e parametre olarak geçirir. Bu sayede LLM sinyalleri state'e **turun sonunda tek bir noktadan** yazılır.

> 💡 **Analiz notu:** Bu class'ın varlık nedeni çok önemli bir bug fix'tir! Eskiden LLM'in intent ve sentiment değerleri turun **ortasında** doğrudan state'e yazılıyordu, sonra turun **sonunda** kural tabanlı çıkarım aynı alanları bir daha yazıyordu — LLM'in kararları sessizce eziliyordu ve `ConsecutiveNegativeTurns` sayacı tur başına 2 artıyordu (3 tur yerine 2 turda otomatik eskalasyon tetikleniyordu).

## 3. Sorumlulukları

- ✅ LLM reasoning sinyallerini turun sonuna **taşımak** (state'e doğrudan yazmadan)
- ✅ "Üretilmedi" durumlarını null ile temsil etmek (kural tabanlı fallback devreye girsin diye)
- ❌ State'i güncellemek — bu `SessionStateExtractor.ExtractAndApply()`'ın işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `TurnSignals.From(ReasoningResult?)` statik factory metodu
- **Kim tüketir:** `SessionStateService.PersistExchangeAsync()` → `SessionStateExtractor.ExtractAndApply()`

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **Çift yazar → tek yazar refactoring:** Bu record, iki ayrı yerden state'e yazma (çift yazar) sorununu çözmek için oluşturuldu. Artık sinyaller state'e yazılmaz, sadece taşınır. State'in tek yazarı `SessionStateExtractor.ExtractAndApply()`'dir.

> 💡 **null semantiği kasıtlıdır:** `Intent = null` demek "LLM intent üretmedi" demektir — bu durumda kural tabanlı çıkarım devreye girer. `Intent = "bilinmiyor"` ise kasıtlı olarak null'a çevrilir çünkü parser JSON'u okuyamadığında bu değeri koyar.

## 6. Metotlar / Üyeler

### Record Parametreleri

| Parametre | Tip | Açıklama |
| ----------- | ----- | ---------- |
| `Intent` | `string?` | LLM'in niyet kararı (null = üretilmedi) |
| `SentimentLabel` | `string?` | LLM'in duygu etiketi (null = üretilmedi) |
| `SentimentScore` | `double?` | LLM'in duygu skoru (null = üretilmedi) |

### Statik Metotlar

| Metot | Açıklama |
|-------|----------|
| `From(ReasoningResult?)` | ReasoningResult'tan sinyalleri süzer — "üretilmedi" değerleri null'a çevirilir |

### From() null Kuralları

- Boş intent veya `WellKnown.Intents.Unknown` → `Intent = null`
- Boş sentiment veya `neutral@0.5` → `SentimentLabel = null`, `SentimentScore = null`
- Her iki sinyal de null ise → tüm TurnSignals null döner

## 7. Constructor Bağımlılıkları

Yok — record tipi.

## Bağlantılar

- [ReasoningResult.md](ReasoningResult.md) — Sinyallerin kaynak modeli
- [SessionState.md](SessionState.md) — Sinyallerin yazılacağı hedef
- [../../CustomerSupportBot.Application/SessionStateService.md](../../CustomerSupportBot.Application/SessionStateService.md) — Sinyalleri işleyen servis
