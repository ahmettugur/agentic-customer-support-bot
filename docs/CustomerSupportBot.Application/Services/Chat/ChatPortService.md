# ChatPortService

**Dosya:** `Services/Chat/ChatPortService.cs`
**Port:** `IChatPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Chat`

## 1. Ne İşe Yarar

Bir chat turunun **tamamını** orkestre eden ana servis: oturumu bulur/oluşturur, kimlik
bağlamını doğrular, geçmişi okur, akıl yürütme (reasoning) ve agent takımını çalıştırır,
sonucu geçmişe yazar. Hem senkron (`HandleAsync`, tek cevap) hem streaming (`HandleStreamAsync`,
SSE/WebSocket için olay akışı) iki giriş noktası sunar.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki `/chat` (senkron) ve `/chat/stream` (SSE) endpoint'leri bu servisi çağırır.
Bir kullanıcı mesaj gönderdiğinde tetiklenen tüm zincirin (oturum → kimlik doğrulama →
akıl yürütme → agent takımı → kalıcılık) tek giriş noktasıdır.

## 3. Sorumlulukları

- **Üstlendiği:** Tur sırasını korumak (`AcquireTurnLockAsync`), oturum kimliğini doğrulamak
  (`BindAuthenticatedCustomerAsync`), akıl yürütme + agent takımını çağırmak, human-mode (HITL
  canlı devralma) sırasında bot'u bypass etmek, sonucu geçmişe yazmak, sentiment olaylarını
  yaymak (streaming yolda).
- **Üstlenmediği:** Akıl yürütmenin kendisi (`IReasoningPort`), agent orkestrasyonunun kendisi
  (`IAgentTeamPort`/[`WorkflowRunner`](../../../../CustomerSupportBot.Adapters.Agents/WorkflowRunner.md)),
  bağlam toplama ([`ContextPipeline`](ContextPipeline.md) — bu, reasoning/agent takımı içinde
  ayrıca çalışır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IChatPort` port'unu implemente eder.
- **Inject eder:** `IAgentTeamPort`, `IReasoningPort`, `ISessionManager`, `IChatModeRegistry`,
  `IChatBridge`, [`SessionStateService`](SessionStateService.md), `IApprovalContextAccessor`,
  `IAppDistributedLock`, `ILogger`.
- [`SessionIdentityBinder`](SessionIdentityBinder.md)'ı kimlik bağlama için kullanır — sesli
  (realtime) kanallar da aynı binder'ı kullanır, kural TEK yerde durur.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Tur kilidi (`AcquireTurnLockAsync`) — neden gerekliydi.** Bir tur "geçmişi oku → akıl
> yürüt → workflow çalıştır → geçmişe yaz" adımlarından oluşur ve bu dizi atomik DEĞİLDİR: aynı
> oturuma aynı anda gelen iki mesaj aynı (eski) geçmişi okuyup sonuçlarını **bitiş sırasına
> göre** (gönderim sırasına göre değil) geçmişe yazabiliyordu. Sonuç iki farklı hata sınıfıydı:
> (1) nedensel bağlam kaybı — ikinci mesaj birincinin cevabını hiç görmeden işlenmiş oluyordu;
> (2) `ForceReplanNextTurn` gibi "tek kullanımlık" oturum bayraklarının tutarsız tüketilmesi.
> Çözüm: her turun başında `IAppDistributedLock` ile oturuma özel bir kilit alınır
> (`SessionIdentityBinder.TurnLockKey(sessionId)`), tur bitene kadar tutulur. Bekleme süresi
> 120sn — bir tur LLM çağrıları yüzünden onlarca saniye sürebileceği için ikinci mesaj
> REDDEDİLMEK yerine SIRAYA girmelidir; kilit tutulduğu sürece kendini yeniler (medallion
> pattern), dolayısıyla uzun turlarda kilit süresi dolup düşmez.
>
> **Neden `session:{id}` değil `SessionIdentityBinder.TurnLockKey`:** Tur içinde
> `MutateStateAsync`/`AddExchangeAsync` zaten `session:{id}` anahtarını farklı bir amaçla
> (state mutasyonu) kısa süreliğine kilitliyor. Aynı anahtar tur kilidi için de kullanılsaydı,
> Redis dağıtık kilidi **yeniden girişli (reentrant) olmadığından** aynı işlem kendi kendini
> kilitlerdi (deadlock). Ayrı bir anahtar (`TurnLockKey`) bu çakışmayı önler; sesli kanallar da
> aynı anahtarı kullandığı için yazılı bir tur ile eşzamanlı bir sesli bağlantı birbirini
> doğru şekilde dışlar.

> 🐞 **Kilit BİNDDEN önce, bind TAZELEMEDEN sonra alınır — sıra önemli.** `BindAuthenticatedCustomerAsync`
> bir "oku → karar ver → yaz" dizisidir (oturum kimseye bağlı değilse çağıranı bağlar). Kilit bu
> dizinin DIŞINDA kalsaydı, sahipsiz aynı oturuma eşzamanlı gelen iki farklı müşteri "bağlı değil"
> okuyup ikisi de bağlanmayı deneyebilirdi (sahiplik yarışı). Kilit alındıktan HEMEN sonra oturum
> `ReloadAsync` ile tazelenir: kilidi beklerken başka bir pod aynı oturumu güncellemiş olabilir —
> Redis pub/sub dinleyicisi cache'e YENİ bir nesne koyar (var olanı mutasyona uğratmaz), yani
> kilit alınmadan önce elde tutulan referans sessizce bayatlamış olabilir. Bayat okuma, iki pod'un
> aynı sahipsiz oturumu birbirinden habersiz bağlamasına yol açardı.

> 🐞 **`BindAuthenticatedCustomerAsync` — sessiz geçişten exception'a.** Eskiden oturum zaten
> BAŞKA bir müşteriye bağlıysa metot sessizce çıkıyor, tur o oturumun (yanlış) kimliğiyle devam
> ediyordu. Yani müşteri B, müşteri A'nın `sessionId`'sini elde edip gönderirse A'nın konuşma
> geçmişini okuyabiliyor, tool'ları A adına çalıştırabiliyordu. Artık ihlalde
> `UnauthorizedSessionAccessException` fırlatılır — sessiz devam etmek yerine tur tamamen durur.

Human-mode dalında (`HandleStreamAsync` içinde), kullanıcı mesajı yazılırken **boş bir asistan
mesajı bırakılmaz** — insan modunda bota ait bir yanıt yoktur, gerçek temsilci cevabı ayrıca
yazılır; eskiden `AddExchangeAsync` boş bir placeholder bırakıyor, bu doldurulmadan kalıp bot
oturumu geri devraldığında bağlamı bozuyordu.

Streaming yolda geçmişe yazılacak metin **önce kanonik `ResponseComplete` metnini** kullanır,
o hiç gelmezse (hata/iptal) delta'ların birleşimine düşer — ikisi FARKLI olabilir çünkü
delta'lar `ResponseAgent`'ın ham akışıdır, kanonik metin teknik JSON'dan temizlenmiş ve
gerekirse ajan-adı sızıntısına karşı yeniden yazılmış halidir. Eskiden yalnızca delta'lar
birleştirildiği için geçmişe ham (temizlenmemiş) metin yazılıyordu.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `HandleAsync(ChatRequest request, CancellationToken ct = default): Task<ChatResponse>` | Senkron tur: oturum → kilit → bind → geçmiş → reasoning → agent takımı → kalıcılık → tek `ChatResponse`. |
| `HandleStreamAsync(ChatRequest request, CancellationToken ct = default): IAsyncEnumerable<StreamEvent>` | Aynı akışın SSE/WebSocket streaming versiyonu; human-mode bypass ve sentiment olayları da burada yayılır. |
| `AcquireTurnLockAsync(string sessionId, CancellationToken ct)` *(private)* | Oturuma özel dağıtık kilit alır (120sn bekleme, medallion self-renew); alınamazsa kullanıcıya "hâlâ işlenen bir mesaj var" hatası döner. |
| `BindAuthenticatedCustomerAsync(AgentSession session, string? customerId, CancellationToken ct)` *(private)* | `SessionIdentityBinder` ile kimlik bağlar; ihlalde `UnauthorizedSessionAccessException` fırlatır. |

## 7. Bağımlılıklar (Constructor Injection)

- `IAgentTeamPort` — agent takımını çalıştırır.
- `IReasoningPort` — akıl yürütme (intent/plan) aşaması.
- `ISessionManager` — oturum okuma/oluşturma/tazeleme/geçmiş.
- `IChatModeRegistry` — bot/human mod durumu.
- `IChatBridge` — canlı sohbet köprüsü (admin/kullanıcı pub/sub).
- `SessionStateService` — sentiment/turu kapatma yardımcıları.
- `IApprovalContextAccessor` — tur bazlı onay bağlamını kurar (`SetScope`).
- `IAppDistributedLock` — tur sıralaması için dağıtık kilit.
- `ILogger<ChatPortService>` — kilit zaman aşımı vb. loglar.

## Bağlantılar

- [ContextPipeline.md](ContextPipeline.md) — bağlam toplama (reasoning/agent takımı içinde çalışır)
- [SessionIdentityBinder.md](SessionIdentityBinder.md) — kimlik bağlama kuralının tek kaynağı
- [SessionStateService.md](SessionStateService.md) — sentiment ve kalıcılık yardımcıları
