# ChatEventOrchestrator

- **Kaynak:** `CustomerSupportBot.Api/Services/ChatEventOrchestrator.cs`
- **Tür:** `public sealed class` (primary constructor)
- **Namespace:** `CustomerSupportBot.Api.Services`

> 🐞 **Bu doküman baştan yazıldı.** Önceki sürüm gerçekte var olmayan bir API tarif ediyordu
> (`ProcessTurnAsync`, `IChatOrchestratorPort`/`IChatBridge` constructor bağımlılıkları vb.) —
> sınıfın gerçek hâliyle hiçbir ortak noktası yoktu. Aşağıdaki içerik doğrudan `.cs` dosyasından
> çıkarıldı.

## 1. Ne İşe Yarar

`GET /chat/events/{sessionId}` kalıcı SSE bağlantısını yöneten orkestratördür. Bir müşteri
sohbet sayfasını açık tuttuğu sürece bu bağlantı üzerinden **oturuma ait tüm canlı olayları**
(temsilci devraldı, eskalasyon beklemede, admin/agent mesajı, bot "yazıyor..." göstergesi, onay
sonuçları) tek bir SSE akışında toplar.

## 2. Hangi Amaçla Kullanılır

[ChatEndpoints.HandleChatEventsAsync](../Endpoints/ChatAndRealtime.md) bu sınıfın tek metodunu
(`ExecuteAsync`) çağırır ve akışın tüm yaşam döngüsünü (açılış → olay yayını → müşteri
ayrılınca temizlik) buraya devreder.

## 3. Sorumlulukları

- **Açılış durumu:** bağlantı kurulur kurulmaz oturumun güncel modunu gönderir — `Human`
  modundaysa `HumanJoined` olayı, `Bot` modunda ve bekleyen bir eskalasyon varsa
  `HandoffPending` olayı (`SendInitialStateAsync`).
- **HITL olay aboneliği:** `IHitlEventPort.SubscribeToChatEvents` ile onay/eskalasyon
  olaylarını dinler.
- **Köprü mesajları:** `IChatSessionPort.SubscribeToUserAsync` ile admin/agent'ın yazdığı
  mesajları ve bot "yazıyor..." göstergesini (`ChatBridgeSender.BotTyping`) dinler
  (`ProcessBridgeMessagesAsync`).
- **Yeniden yetkilendirme (kritik):** her tek olay yazımından ÖNCE `stillAuthorized` callback'ini
  çağırır (`GuardedWriteAsync`); sahiplik değişmişse akışı iptal edip kapatır.
- **Orphan eskalasyon temizliği:** akış kapanınca (müşteri sayfadan ayrıldığında), uygulama
  kapanıyor OLMADIĞI sürece o oturuma ait sahipsiz kalmış eskalasyonları
  (`chatSession.DismissOrphanedEscalations`) otomatik kapatır.
- **Üstlenmediği:** oturum modunun (`Bot`/`Human`) DEĞİŞTİRİLMESİ (takeover/release —
  [AdminAndHitl.md](../Endpoints/AdminAndHitl.md)'nin işi), bot yanıtlarının üretimi (`IChatPort`'un
  işi — bu sınıf yalnızca köprü/HITL olaylarını taşır, ana sohbet akışını değil).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IChatSessionPort` (Application, Inbound) — oturum durumu, açık eskalasyonlar, köprü mesaj
  aboneliği, orphan temizliği.
- `IHitlEventPort` (Application, Inbound) — onay/eskalasyon canlı olay aboneliği.
- `IHostApplicationLifetime` — uygulama kapanıyorsa orphan-eskalasyon temizliğini ATLAMAK için
  (aşağıya bakınız).
- [SseForwarder](../Infrastructure/SseAndWebSockets.md) — olayların gerçekten yazıldığı taşıyıcı.
- [ChatAndRealtime.md](../Endpoints/ChatAndRealtime.md) — tek çağıran (`ChatEndpoints`).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **`stillAuthorized` her yazımdan önce yeniden kontrol edilir, bağlantı açılışında bir kez
  değil:** kod yorumu bunu açıkça gerekçelendiriyor — henüz kimseye bağlı olmayan bir oturuma
  abone olmak serbesttir (ilk temas oturumu çağırana bağlar), ama oturum SONRADAN başka bir
  müşteriye bağlanabilir. Tek seferlik kontrol olsaydı, açık kalan eski bir akış yeni sahibinin
  bot yanıtlarını/temsilci mesajlarını/onay sonuçlarını YANLIŞ dinleyiciye akıtmaya devam
  ederdi.
- **Orphan eskalasyon temizliği `appLifetime.ApplicationStopping` kontrolüyle korunur:**
  uygulama KAPANIRKEN tüm SSE bağlantıları aynı anda kesilir — bu durumda her bağlantının
  "müşteri ayrıldı" sanıp kendi eskalasyonunu kapatması yanlış bir kütle-kapatma dalgası
  yaratırdı (müşteri aslında hâlâ bağlıydı, sadece sunucu yeniden başlıyordu). Kontrol bu yanlış
  pozitifi önler.
- **`CancellationTokenSource.CreateLinkedTokenSource`:** hem istemci bağlantı kopması (`ct`) hem
  sahiplik kaybı (`GuardedWriteAsync`'in tetiklediği iptal) aynı token'ı paylaşır — akışın her
  iki nedenle de aynı şekilde (temiz bir şekilde) sonlanmasını sağlar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ChatEventOrchestrator(IChatSessionPort, IHitlEventPort, IHostApplicationLifetime, ILogger<ChatEventOrchestrator>)` | Primary constructor (record-benzeri kısa syntax). |
| `Task ExecuteAsync(string sessionId, SseForwarder sse, Func<CancellationToken, Task<bool>> stillAuthorized, CancellationToken ct)` | Akışın tüm yaşam döngüsünü yürütür: açılış durumu → HITL aboneliği → köprü mesajları → (bağlantı kapanınca) orphan temizliği. İstemci bağlanı kesene/iptal edilene kadar döner. |
| `SendInitialStateAsync(string sessionId, Func<string, object?, Task> write)` *(private)* | Bağlantı açılışında güncel modu/bekleyen eskalasyonu bir kerelik gönderir. |
| `ProcessBridgeMessagesAsync(string sessionId, Func<string, object?, Task> write, CancellationToken ct)` *(private)* | Admin/agent mesajlarını ve bot-typing göstergesini sürekli dinler ve yazar. |

## 7. Bağımlılıklar

| Bağımlılık | Neden |
|---|---|
| `IChatSessionPort` | Oturum durumu, köprü mesaj aboneliği, orphan eskalasyon temizliği. |
| `IHitlEventPort` | Onay/eskalasyon canlı olayları. |
| `IHostApplicationLifetime` | Uygulama kapanışını orphan-temizliği yanlış tetiklemeden ayırt etmek için. |
| `ILogger<ChatEventOrchestrator>` | Sahiplik değişikliği/bağlantı kapanışı/eskalasyon temizliği loglaması. |

DI ömrü: `Scoped` (bkz. [ApplicationServicesExtensions](../Extensions/ApplicationServicesExtensions.md)).

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [../Endpoints/ChatAndRealtime](../Endpoints/ChatAndRealtime.md)
- [../Infrastructure/SseAndWebSockets](../Infrastructure/SseAndWebSockets.md)
