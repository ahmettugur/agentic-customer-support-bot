# ChatSessionPortService

**Dosya:** `Services/Chat/ChatSessionPortService.cs`
**Port:** `IChatSessionPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Chat`

## 1. Ne İşe Yarar

Admin panelinin **canlı devralma (human takeover)** ve **yeniden planlama (replan)**
işlemlerini orkestre eder: bir temsilcinin bir oturumu bot'tan devralması/bırakması, temsilci
mesajı göndermesi, sohbet geçmişini/duygu durumunu okuması, açık eskalasyonları yönetmesi.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki admin/agent panel endpoint'leri (canlı sohbet ekranı) bu servisi çağırır:
oturum listesini göstermek, bir oturumu devralmak, temsilci mesajı yazmak, bir eskalasyonu
"yeniden planla" ile bota geri döndürmek.

## 3. Sorumlulukları

- **Üstlendiği:** Mod geçişlerini (`TakeOver`/`Release`) `IChatModeRegistry`'ye yaptırmak ve
  yan etkilerini (sistem mesajı yayınlama, eskalasyon kapatma, temsilci yükünü artırma/azaltma)
  koordine etmek; replan akışında oturum bayraklarını (`ForceReplanNextTurn` vb.) kurup
  `IReplanService`'i tetiklemek.
- **Üstlenmediği:** Mod durumunun kalıcılığı (`IChatModeRegistry`), gerçek replan LLM çağrısı
  (`IReplanService`), sohbet mesajlarının dağıtımı (`IChatBridge`).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IChatSessionPort` port'unu implemente eder.
- **Inject eder:** `IChatModeRegistry`, `IChatBridge`, `ISessionManager`, `IEscalationSink`,
  `IHumanAgentRegistry`, `IReplanService`.
- **Kimin tarafından çağrılır:** Api katmanındaki admin panel endpoint'leri (canlı devralma,
  temsilci mesajlaşma, yeniden planlama uçları).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**`SendAdminMessageAsync` — neden `AppendAssistantMessageAsync`, `AppendUserMessageAsync` değil:**
Temsilcinin insan modunda yazdığı mesaj **asistan rolüyle** geçmişe yazılır, kullanıcı rolüyle
DEĞİL. Gerekçe kodda açıkça belirtilir: insan modunda temsilci konuşmada botun yerini alır;
söylediği şey müşteriden gelen bir GİRDİ değil, müşteriye verilen bir YANITTIR. Kullanıcı
rolüyle yazılsaydı, bot oturumu geri devraldığında temsilcinin cevabını müşterinin yeni bir
mesajı sanıp ona "cevap vermeye" çalışırdı — bağlam bozulurdu. Mesaj etiketlenerek
(`[🧑‍💼 {agentLabel}]: ...`) yazılır ki model bunu kendi ürettiği bir yanıt değil, insan
temsilcinin sözü olarak görsün.

**Replan akışının adımları (`ReplanSessionAsync`/`ReplanEscalationAsync`) neden bu sırada:**
1. Oturuma replan bayrakları yazılır (`ApplyReplanState`) — bir sonraki turda reasoning'in
   yeniden planlama yapması için.
2. Açık eskalasyonlar çözülür (bir replan, o oturuma ait bekleyen "insan gerekiyor" taleplerini
   geçersiz kılar).
3. Human mode'daysa bot moduna serbest bırakılır (`ReleaseIfHumanMode`) — replan bot'un işi
   olduğu için insan modunda kalması anlamsız.
4. Admin'e özel + müşteriye görünür sistem mesajları yayınlanır.
5. `IReplanService.ExecuteAsync` **`_ = ...` ile ateşle-unut (fire-and-forget)** çağrılır —
   replan LLM çağrısı gerektirebilir ve admin'in "yeniden planla" isteğinin HTTP cevabı bunu
   beklememelidir; müşteri replan sonucunu normal sohbet akışında görecektir.

İki farklı replan giriş noktası (`ReplanSessionAsync` doğrudan sessionId ile,
`ReplanEscalationAsync` bir eskalasyon kaydı üzerinden) aynı `ApplyReplanState` yardımcı
metodunu paylaşır — replan mantığının iki farklı tetikleyicide birbirinden sapmaması için.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `GetActive(): IReadOnlyList<ChatSessionState>` | Şu an human/bot modda olan tüm oturumları döner. |
| `GetStateOrDefault(string sessionId): ChatSessionState` | Oturumun mod durumunu döner; kayıt yoksa varsayılan `Bot` modu üretir. |
| `GetHistory(string sessionId, int take = 50): IReadOnlyList<ChatBridgeMessage>` | Sohbet köprüsü geçmişini döner. |
| `GetSentimentAsync(string sessionId, CancellationToken ct = default): Task<ChatSessionSentimentSnapshot?>` | Oturumun güncel duygu durumu + son 10 kayıt. |
| `PublishSystemMessage(string sessionId, string text): void` | Sohbet köprüsüne sistem mesajı yayınlar. |
| `TakeOver(string sessionId, string humanAgent, string? agentId = null): ChatSessionTakeoverResult` | Oturumu bota devralır (Human moda geçer), açık eskalasyonları onaylar, temsilci yükünü artırır. |
| `Release(string sessionId, string? agentId = null): ChatSessionReleaseResult` | Oturumu bota geri bırakır, açık eskalasyonları çözer, temsilci yükünü azaltır. |
| `SendAdminMessageAsync(string sessionId, string humanAgent, string text, CancellationToken ct = default): Task<ChatSessionMessageResult>` | Yalnızca Human moddaki oturumlarda temsilci mesajı yayınlar ve asistan rolüyle geçmişe yazar. |
| `ReplanSessionAsync(string sessionId, string requestedBy, string? note, CancellationToken ct = default): Task<ChatSessionReplanResult>` | Belirtilen oturumu yeniden planlamaya zorlar. |
| `ReplanEscalationAsync(string escalationId, string requestedBy, string? note, CancellationToken ct = default): Task<ChatSessionReplanResult>` | Bir eskalasyon kaydı üzerinden bağlı oturumu yeniden planlamaya zorlar. |
| `SubscribeToAdminAsync`/`SubscribeToUserAsync(string sessionId, CancellationToken ct): IAsyncEnumerable<ChatBridgeMessage>` | Canlı sohbet köprüsüne (admin veya kullanıcı tarafı) abone olur. |
| `GetOpenEscalations(): IReadOnlyList<EscalationRequest>` | Açık eskalasyonları döner. |
| `DismissOrphanedEscalations(string sessionId): int` | Bağlantısı kesilen müşterinin eskalasyonlarını reddeder. |

## 7. Bağımlılıklar (Constructor Injection)

- `IChatModeRegistry` — bot/human mod durumu.
- `IChatBridge` — canlı mesaj yayınlama.
- `ISessionManager` — oturum okuma/güncelleme.
- `IEscalationSink` — eskalasyon kayıtları.
- `IHumanAgentRegistry` — temsilci yük takibi.
- `IReplanService` — yeniden planlama LLM çağrısı.

## Bağlantılar

- [ChatPortService.md](ChatPortService.md) — sohbet turunun ana orkestrasyonu
- [../Escalation/EscalationPortService.md](../Escalation/EscalationPortService.md) — eskalasyon yönetimi
