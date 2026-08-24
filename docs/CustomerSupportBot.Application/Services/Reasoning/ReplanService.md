# ReplanService

- **Kaynak:** `Services/Reasoning/ReplanService.cs`
- **Tür:** `public sealed class : IReplanService`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## 1. Ne İşe Yarar

[`IReplanService`](IReplanService.md)'in tek implementasyonu. Bir oturumdaki son kullanıcı
mesajını (veya varsa admin notunu) reasoning + agent-team pipeline'ından yeniden geçirir,
üretilen yanıtı oturum geçmişine yazar ve `IChatBridge` üzerinden **canlı** olarak müşteriye
bot mesajı gibi yayınlar.

## 2. Hangi Amaçla Kullanılır

Admin panelinden bir sohbete müdahale edilip ("Yeniden Planla" + isteğe bağlı bir not) botun
o oturum için yeni bir yanıt üretmesi istendiğinde arka planda çalıştırılır — kullanıcı hiçbir
şey yazmadan.

## 3. Sorumlulukları

**Üstlendiği:**
- Oturumu ve geçmişi okumak; `session.State.ReplanNote` varsa onu, yoksa son kullanıcı
  mesajını "etkin sorgu" (`effectiveQuery`) olarak seçmek.
- Reasoning'i çalıştırmak, ardından `IApprovalContextAccessor.SetScope` ile HITL bağlamını
  kurup agent-team'i çalıştırmak.
- Sonucu geçmişe yazmak (`AppendAssistantMessageAsync`) ve `IChatBridge` ile "yazıyor"/"mesaj"/
  hata durumlarını yayınlamak.
- Her adımda oluşabilecek hataları yutup kullanıcıya (mümkünse) nazik bir sistem mesajı
  göstermek — arka plan işi olduğu için hiçbir istisna dışarı sızmamalı.

**Üstlenmediği:** Reasoning/routing mantığının kendisi (`IReasoningPort`/`IAgentTeamPort`'un işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ISessionManager` — oturum/geçmiş okuma, yanıtın geçmişe yazılması.
- `IChatBridge` — "bot yazıyor" göstergesi, bot mesajı ve sistem hata mesajı yayını (admin
  panelinin canlı izlemesi için).
- `IAgentTeamPort`, `IReasoningPort` — yazılı/sesli sohbetle **aynı** pipeline.
- `IApprovalContextAccessor` — HITL onay bağlamına oturum/kimlik bilgisini taşımak.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### Admin notu neden `effectiveQuery` olarak önceliklidir

`session.State.ReplanNote` varsa, Reasoning/Planning ajanları bunu **gerçek müşteri talebi**
gibi işler — admin, "müşteri aslında X'i kastetmiş olmalı" gibi bir düzeltme notu bırakabilir.
Eski kullanıcı mesajı geçmişte (`history`) bağlam olarak kalmaya devam eder, silinmez.

### Hatalar neden tamamen yutulur

`ExecuteAsync` **arka planda**, kullanıcının doğrudan beklemediği bir HTTP isteği dışında
çalışır — bir istisnanın dışarı sızması hiçbir yere raporlanamaz. Bu yüzden dış `catch` bloğu
hatayı loglar ve mümkünse `IChatBridge.PublishSystemMessage` ile kullanıcıya nazik bir "tekrar
yazar mısınız?" mesajı gösterir; bu ikinci yayın bile başarısız olursa (`try/catch` iç içe)
sessizce yutulur — replan'ın kendisi zaten en-iyi-çaba (best-effort) bir mekanizmadır.

### `PublishBotTyping` neden `finally` içinde kapatılır

Reasoning veya agent-team çağrısı hata verse bile "yazıyor" göstergesinin sonsuza kadar açık
kalmaması için `finally` bloğunda `false` ile kapatılır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ExecuteAsync(sessionId, ct)` | Oturum yoksa veya etkin sorgu boşsa sessizce döner. Aksi halde: `PublishBotTyping(true)` → reasoning → approval scope kurup agent-team çalıştırma → boş yanıtsa uyarı loglayıp dönme → geçmişe yazma + `PublishBotMessage` → `finally`'de `PublishBotTyping(false)`. Dış `catch` tüm istisnaları loglar ve best-effort bir sistem mesajı yayınlamayı dener. |

## 7. Bağımlılıklar

Constructor injection ile: `ISessionManager`, `IChatBridge`, `IAgentTeamPort`, `IReasoningPort`,
`IApprovalContextAccessor`, `ILogger<ReplanService>`.

## Bağlantılar

- [IReplanService.md](IReplanService.md) — sözleşme
