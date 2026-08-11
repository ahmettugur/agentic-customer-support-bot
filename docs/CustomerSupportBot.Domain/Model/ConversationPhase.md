# ConversationPhase

**Dosya:** `Model/ConversationPhase.cs`  
**Tür:** `enum`

## 1. Ne İşe Yarar

Konuşmanın mevcut aşamasını belirler: karşılama → bilgi toplama → aksiyon → çözüm.

## 2. Hangi Amaçla Kullanılır

`SessionStateExtractor` her turda fazı günceller. Fazlar, reasoning prompt'unda bağlam olarak kullanılır ve admin panelinde konuşma durumunu görselleştirmek için raporlanır.

> 💡 **Analiz notu:** Bir telefon görüşmesinin aşamaları gibi düşün: önce "merhaba" (Greeting), sonra "siparişin numarası ne?" (Inquiry), ardından "bakıyorum efendim" (Action), en son "siparişiniz kargoya verildi" (Resolution).

## 3. Sorumlulukları

- ✅ Konuşma aşamasını type-safe olarak temsil etmek
- ❌ Faz geçiş mantığını yürütmek — bu `SessionStateExtractor`'ın işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Nerede kullanılır:** `SessionState.Phase` (ama Phase string'dir — `WellKnown.Phases` sabitleri üzerinden; bu enum string yerine type-safe alternatif)
- **Kim günceller:** `SessionStateExtractor`

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 `SessionState.Phase` hâlâ `string` tipindedir çünkü JSON serialization uyumluluğu gerekir. Bu enum ise **type-safe kontrol** ve **kod okunabilirliği** için eklenmiştir. `WellKnown.Phases` string sabitlerini içerir — karşılaştırmalarda magic string yerine bunlar kullanılır.

## 6. Enum Değerleri

| Değer | Tipik Turn | Açıklama |
| ------- | ------------ | ---------- |
| `Greeting` | Turn 1 | Karşılama / ilk temas — "Merhaba, nasıl yardımcı olabilirim?" |
| `Inquiry` | Turn 2-3 | Bilgi toplama — "Sipariş numaranızı alabilir miyim?" |
| `Action` | Turn 3-5 | Aksiyon alınıyor — tool çağrısı, DB sorgusu, sipariş oluşturma |
| `Resolution` | Son turn | Sonuç / çözüm — "Siparişiniz kargoya verildi" |

## 7. Constructor Bağımlılıkları

Yok — enum tipi.

## Bağlantılar

- [SessionState.md](SessionState.md) — Phase alanı
- [../WellKnown.md](../WellKnown.md) — `WellKnown.Phases` string sabitleri
- [../Services/SessionStateExtractor.md](../Services/SessionStateExtractor.md) — Fazı kim günceller
