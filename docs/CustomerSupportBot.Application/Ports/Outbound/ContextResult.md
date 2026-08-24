# ContextResult (+ ContextPart, ContextPartStatus)

**Kaynak:** `Ports/Outbound/ContextResult.cs`

## 1. Ne İşe Yarar

[`IContextPipeline.BuildContextAsync`](IContextPipeline.md)'ın dönüş tipi. Yalnızca birleşik
bağlam metnini değil, **hangi provider'ın ne yaptığını** da taşır.

## 2. Hangi Amaçla Kullanılır

`WorkflowMessageBuilder` (Adapters.Agents) geçmiş kırpma kararını `ContextResult.Included(...)`
ile verir; `ReasoningTrace.ContextParts` gözlemlenebilirlik için bu parçaları saklar.

## 3. Sorumlulukları

- **Üstlendiği:** Birleşik metni + provider bazlı durum/boyut raporunu taşımak.
- **Üstlenmediği:** Provider'ların çalıştırılması — bu `ContextPipeline`'ın işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Detaylı akış için bkz. [ContextPipeline.md](../../Chat/ContextPipeline.md) §3.4 (Raporlama).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Düz `string` değil bir sonuç nesnesi olmasının nedeni: çağıranın **hangi provider'ın katkı
yaptığını** bilmesi gerekir. Somut sebep `WorkflowMessageBuilder`'ın geçmiş kırpma kararı —
özetlenen turlar yalnızca özet BU TURDA gerçekten prompt'a girdiyse atlanabilir. Karar oturum
durumuna bakarak verilseydi, özetleyici hata verdiği turda özet de geçmiş de prompt'ta olmaz
ve o turlar tamamen kaybolurdu. İkinci fayda gözlemlenebilirlik: parçalar trace'e yazılınca
"model neyi biliyordu?" sorusu yanıtlanabilir hale gelir.

## 6. Metotlar / Üyeler

**`ContextPart(string ProviderName, int Order, ContextPartStatus Status, int Length)`** — bir
provider'ın bu turdaki sonucu.

**`ContextPartStatus`** enum: `Included` (prompt'a kondu), `Empty` (normal, söyleyecek bir şey
yoktu), `Failed` (hata verdi), `TimedOut` (süresi doldu), `Dropped` (bütçe dolduğu için
dışarıda bırakıldı).

**`ContextResult(string Text, IReadOnlyList<ContextPart> Parts)`**

| Üye | Açıklama |
|---|---|
| `static readonly ContextResult Empty` | Boş bağlam sabiti. |
| `bool Included(string providerName)` | Adı verilen provider bu turda prompt'a gerçekten katkı yaptı mı? |

## 7. Bağımlılıklar

Yok — saf veri modeli.
