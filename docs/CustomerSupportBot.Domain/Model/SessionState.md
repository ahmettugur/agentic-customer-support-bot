# SessionState

**Dosya:** `Model/AgentSession.cs` (AgentSession ile aynı dosyada)  
**Tür:** `class` (mutable)  
**İlişkili:** `SentimentEntry` (aynı dosyada)

## 1. Ne İşe Yarar

Bir oturumun **türetilmiş durumunu** tutar — niyet, faz, duygu, toplanan bilgiler, replan bilgileri. `AgentSession.State` property'si olarak yaşar.

## 2. Hangi Amaçla Kullanılır

Her konuşma turunda `SessionStateExtractor` bu nesneyi günceller. Ajanlar arası paylaşılan bağlam olarak kullanılır — örneğin PlanningAgent mevcut intent'i, specialist agent'lar toplanan entity'leri buradan okur.

> 💡 **Analiz notu:** Bir müşteri destek görüşmesinde temsilcinin not defteri gibi düşün. Her turda "müşteri ne istiyordu (intent), neler topladık (collectedInfo), müşteri sinirli mi (sentiment)" bilgisi güncellenir.

## 3. Sorumlulukları

- ✅ Oturum boyunca toplanan tüm bağlamı taşımak (single source of truth)
- ✅ Sentiment geçmişi tutmak (trend analizi için)
- ✅ Admin replan flag'lerini taşımak
- ❌ State'i güncelleme mantığını içermek (bu `SessionStateExtractor`'ın işi)

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Yaşadığı yer:** `AgentSession.State` property'si olarak
- **Kim günceller:** `SessionStateExtractor.ExtractAndApply()` — her turda
- **Kim okur:**
  - `ReasoningMessageBuilder` — reasoning prompt'una state bilgisi ekler
  - `EntityVerifier` — müşteri kimliği için yalnız `AuthenticatedCustomerId`'yi kullanır
  - `WorkflowMessageBuilder` — ajan workflow bağlamına intent/phase ekler
  - `EscalationPolicyService` — `ConsecutiveNegativeTurns` eşik kontrolü
  - `IReplanService` — `ForceReplanNextTurn` flag'ini okur

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **İki farklı CustomerId alanı var — bu kasıtlı!**
>
> - `CustomerId`: LLM'in kullanıcının metninden çıkardığı numara. **Güvenli değildir** — kullanıcı "ben 1008 numaralı müşteriyim" diyerek başka birinin bilgilerini sızdırabilir.
> - `AuthenticatedCustomerId`: JWT token'dan gelen, login ile doğrulanmış kimlik. **Güvenilirdir** — onay gerektiren tool'lar (sipariş, iade) sadece bunu kullanır.
>
> Bu ayrım güvenlik açığı düzeltilirken eklenmiştir.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `CustomerId` | `string?` | LLM'in metinden çıkardığı müşteri ID (güvenli değil — kişiselleştirme için) |
| `AuthenticatedCustomerId` | `string?` | JWT'den doğrulanmış müşteri ID (güvenilir — onay tool'ları için) |
| `CurrentIntent` | `string?` | Mevcut niyet (ör: "sipariş_sorgulama") |
| `CollectedInfo` | `Dictionary<string, string>` | Toplanan bilgiler (key-value: customer_id, order_id vb.) |
| `TurnCount` | `int` | Toplam tur sayısı |
| `ConversationSummary` | `string?` | Uzun konuşmaların LLM tarafından üretilmiş özeti |
| `Phase` | `string` | Konuşma fazı (greeting, inquiry, action, resolution) |
| `Sentiment` | `string` | Mevcut duygu etiketi (positive, neutral, negative, angry) |
| `SentimentScore` | `double` | Duygu skoru (0.0 = çok olumsuz, 1.0 = çok olumlu) |
| `SentimentHistory` | `List<SentimentEntry>` | Son N tur için duygu geçmişi |
| `ConsecutiveNegativeTurns` | `int` | Ardışık negatif tur sayısı (otomatik eskalasyon tetikleyici) |
| `ForceReplanNextTurn` | `bool` | Admin "Yeniden Planla" tetiklediğinde true olur (one-shot) |
| `ReplanRequestedBy` | `string?` | Replan'ı tetikleyen admin adı |
| `ReplanRequestedAt` | `DateTime?` | Replan işaretlenme anı |
| `ReplanNote` | `string?` | Admin'in PlanningAgent'a iletmek istediği not (müşteriye gösterilmez) |

### CollectedInfo Örnek

```csharp
state.CollectedInfo = new()
{
    ["customer_id"] = "12345",
    ["order_id"] = "5",
    ["product_name"] = "Dell XPS 15"
};
```

Specialist agent'lar bu Dictionary'yi okur, tool parametresi olarak kullanır.

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı. Varsayılan değerlerle başlatılır.

---

# SentimentEntry

**Tür:** `class`

Tek bir tur için duygu kaydı. `SessionState.SentimentHistory` listesinin elemanıdır.

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Turn` | `int` | Hangi turda kaydedildi |
| `Label` | `string` | Duygu etiketi (WellKnown.Sentiments sabitlerinden) |
| `Score` | `double` | 0.0–1.0 arası duygu skoru |
| `Timestamp` | `DateTime` | Kaydedilme zamanı |

## Bağlantılar

- [AgentSession.md](AgentSession.md) — Bu state'i taşıyan oturum nesnesi
- [../Services/SessionStateExtractor.md](../Services/SessionStateExtractor.md) — State'i güncelleme mantığı
- [../../CustomerSupportBot.Application/SessionStateService.md](../../CustomerSupportBot.Application/Services/Chat/SessionStateService.md) — State persist eden Application servisi
- [../WellKnown.md](../WellKnown.md) — Phase, Sentiment, Intent sabitleri
