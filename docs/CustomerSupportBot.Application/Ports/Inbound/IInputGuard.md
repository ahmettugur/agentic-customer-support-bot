# IInputGuard, InputGuardVerdict, InputGuardResult

**Dosya:** `Ports/Inbound/IInputGuard.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Kullanıcıdan gelen ham metni, workflow'a/LLM'e geçmeden önce denetleyen guard'ın sözleşmesi — prompt injection, aşırı uzun girdi, zararlı içerik gibi riskleri tespit edip metni izin ver/temizle/reddet kararına bağlar.

## 2. Hangi amaçla kullanılır?

`ChatPortService`/`WorkflowRunner` bir kullanıcı mesajını işlemeye başlamadan önce `Inspect` çağrısıyla mesajı denetler; realtime (sesli) modda da `create_response` öncesi bu kontrolün tamamlanması beklenir (bkz. bilinen kısıt notu `OpenAiRealtimeClientAdapter.md`).

## 3. Sorumlulukları

- **Üstlendiği:** Tek bir senkron karar noktası sunmak: girdi güvenli mi, temizlenmiş hali nedir, reddedilme sebebi nedir.
- **Üstlenmediği:** Karar algoritmasının kendisi (regex/heuristik/LLM-tabanlı olması) — arayüz bunu saklar, implementasyon Adapters/Application katmanındadır.

## 4. Diğer katman/bileşenlerle ilişkileri

- `ChatPortService`, realtime bridge'ler (`IRealtimeBridge`/`IRealtimeNativeBridge` implementasyonları) tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Üç durumlu (`Allow`/`Sanitize`/`Reject`) bir sonuç modeli, ikili (`bool`) bir sonuçtan daha esnektir: şüpheli ama tamamen zararlı olmayan girdiler tamamen reddedilmek yerine temizlenip (`SanitizedInput`) işlenmeye devam edebilir — kullanıcı deneyimini gereksiz yere kesmeden riski azaltır. `Flags` alanı, hangi kuralın tetiklendiğini trace/log'a yazmak için vardır (denetlenebilirlik).

## 6. Tipler ve Üyeler

### `enum InputGuardVerdict { Allow, Sanitize, Reject }`
Denetim sonucunun üç olası kararı.

### `InputGuardResult(InputGuardVerdict Verdict, string SanitizedInput, IReadOnlyList<string> Flags, string? RejectionReason)`
Denetim sonucunun tam çıktısı — karar, (varsa) temizlenmiş metin, tetiklenen bayraklar, (reddedildiyse) sebep.

### `IInputGuard`

| Metot | Açıklama |
|---|---|
| `InputGuardResult Inspect(string? input)` | Girdi metnini senkron olarak denetler ve kararı döner. |

## 7. Bağımlılıklar

Yok (arayüz düzeyinde) — saf bir denetim sözleşmesi.

## Bağlantılar

- [OpenAiRealtimeClientAdapter](../../Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.md) — `Inspect` tamamlanmadan `create_response=true` ile modelin cevap üretebilmesi bilinen bir kısıt olarak orada belgelenmiştir.
