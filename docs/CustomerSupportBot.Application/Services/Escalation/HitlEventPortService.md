# HitlEventPortService

**Dosya:** `Services/Escalation/HitlEventPortService.cs`
**Port:** `IHitlEventPort` (driving/inbound port)
**Namespace:** `CustomerSupportBot.Application.Services.Escalation`

## 1. Ne İşe Yarar

Bir oturuma özel HITL (human-in-the-loop) olaylarına (onay talebi/karar, eskalasyon,
human-mode giriş/çıkış) **canlı abonelik** sağlar. İki farklı abonelik türü sunar —
`Subscribe` (geçici `/chat/stream` bağlantısı için) ve `SubscribeToChatEvents` (kalıcı
`/chat/events/{sessionId}` bağlantısı için) — çünkü ikisinin dinlediği olay kümesi farklıdır.

## 2. Hangi Amaçla Kullanılır

Api katmanındaki SSE/WebSocket adaptörleri, bir sohbet bağlantısı açıldığında bu servisten
bir `IHitlEventSubscription` alır; bağlantı kapandığında `Dispose()` ile aboneliği bırakır.

## 3. Sorumlulukları

- **Üstlendiği:** Oturuma özel event filtreleme (yalnızca `SessionId` eşleşen olayları
  iletmek), olay işleyicisinin (`onEvent`) hata almasını sessizce yutmadan loglamak
  (`FireAndForget`), abonelik yaşam döngüsünü (`IDisposable`) yönetmek.
- **Üstlenmediği:** Olayların kaynağı (`IApprovalQueue`/`IEscalationSink`/`IChatModeRegistry`
  zaten var olan olayları yayınlar, bu sınıf yalnızca filtreleyip yönlendirir).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IHitlEventPort` port'unu implemente eder.
- **Inject eder:** `IApprovalQueue`, `IEscalationSink`, `IChatModeRegistry`.
- **Kimin tarafından çağrılır:** Api katmanındaki SSE (`/chat/stream`) ve kalıcı olay akışı
  (`/chat/events/{sessionId}`) adaptörleri.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**İki ayrı iç abonelik sınıfı (`ApprovalEscalationSubscription`, `ChatEventSubscription`)
neden var, tek bir genel sınıf değil:**

- `ApprovalEscalationSubscription`, **geçici** `/chat/stream` bağlantısı içindir — bir turun
  süresi boyunca yaşar. `ApprovalRequired`/`ApprovalResolved`/`EscalationCreated` olaylarını dinler.
- `ChatEventSubscription`, **kalıcı** `/chat/events/{sessionId}` bağlantısı içindir — sayfa
  açık kaldığı sürece yaşar, insan devri (`HumanJoined`/`HumanLeft`) ve devir bekleyen/temizlenen
  durumları (`HandoffPending`/`HandoffCleared`) da dinler; bunlar bir turun ötesinde, oturumun
  TÜM yaşam döngüsü boyunca anlamlı olan olaylardır.

> 🐞 **`ChatEventSubscription`'ın `ApprovalResolved`'ı dinlemesi — bloklamayan onay modelinin
> bildirim ayağı.** Admin onayı gerektiren 4 tool (sipariş verme, şikayet, iptal, iade) artık
> admin kararını **senkron beklemez** — hemen "onaya gönderildi" döner ve tur biter. Karar daha
> sonra, admin panelden geldiğinde, `IApprovalQueue.RequestDecided` olayı tetiklenir. Kullanıcı
> o sırada sayfayı açık tutuyorsa (kalıcı bağlantı), bu olay `ApprovalResolved` olarak anında
> iletilir — `executionResult`/`executionStatus` alanları dahil, yani kullanıcı sadece
> "onaylandı" değil, "onaylandı VE gerçekten işlendi" bilgisini de görür. Bu, admin onayının
> artık bir HTTP isteğini bloklamadığı, bunun yerine bir bildirim/badge akışına dönüştüğü
> tasarımın canlı-bağlı taraftaki karşılığıdır.

Her iki iç sınıf da `FireAndForget` yardımcısını kullanır: `onEvent` delegesi çağrılırken
oluşan bir hata **sessizce yutulmaz**, `ContinueWith(..., OnlyOnFaulted)` ile loglanır — ama
olayın kendisi (ör. bir SSE yazma hatası) diğer event handler'ların çalışmasını ENGELLEMEZ,
çünkü olaylar zaten `_ = task` ile ateşle-unut şeklinde tetiklenir.

`Dispose()`, constructor'da eklenen TÜM event handler'ları (`-=`) kaldırır — bu, abonelik
sızıntısını (memory leak) önlemenin standart yoludur; her `+=` bağlantısının kapanışta bir
`-=` karşılığı vardır (bkz. [`ChatBridge`](../../../../CustomerSupportBot.Adapters.Persistence/README.md)'deki
benzer `Unregister` deseni).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Subscribe(string sessionId, Func<string, object, Task> onEvent): IHitlEventSubscription` | Geçici bağlantı için abonelik: `ApprovalRequired`, `ApprovalResolved`, `EscalationCreated`. |
| `SubscribeToChatEvents(string sessionId, Func<string, object, Task> onEvent): IHitlEventSubscription` | Kalıcı bağlantı için abonelik: `ApprovalResolved`, `HumanJoined`/`HumanLeft`, `HandoffPending`/`HandoffCleared`. |

### İç sınıflar (private, `IHitlEventSubscription` implementasyonları)

| Sınıf | Dinlediği olaylar |
|---|---|
| `ApprovalEscalationSubscription` | `IApprovalQueue.RequestCreated/RequestDecided`, `IEscalationSink.RequestCreated`. |
| `ChatEventSubscription` | `IApprovalQueue.RequestDecided`, `IChatModeRegistry.ModeChanged`, `IEscalationSink.RequestCreated/RequestDecided`. |

## 7. Bağımlılıklar (Constructor Injection)

- `IApprovalQueue` — onay talebi/karar olayları.
- `IEscalationSink` — eskalasyon olayları.
- `IChatModeRegistry` — bot/human mod geçiş olayları.
- `ILogger<HitlEventPortService>?` *(opsiyonel, `NullLogger` varsayılan)*.

## Bağlantılar

- [EscalationPortService.md](EscalationPortService.md) — admin panelin eskalasyon CRUD'u
- [../Approval/ApprovalPortService.md](../Approval/ApprovalPortService.md) — onay kuyruğu orkestrasyonu
