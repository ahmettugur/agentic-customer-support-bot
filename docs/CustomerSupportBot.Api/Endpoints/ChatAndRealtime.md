# Sohbet ve Gerçek Zamanlı Uç Noktaları (Chat & Realtime)

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Endpoints/ChatEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/RealtimeEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/SessionEndpoints.cs`
- **Namespace:** `CustomerSupportBot.Api.Endpoints`

> 🐞 **Bu doküman kapsamlı biçimde güncellendi.** Önceki sürüm `/api/chat`, `/api/sessions` gibi
> yolları ve anonim erişimi tarif ediyordu — kod artık **müşteri login'i zorunlu** (`RequireAuthorization("Customer")`)
> ve gerçek route'lar `/chat/`, `/chat/stream`, `/chat/realtime/{sessionId?}` biçimindedir.
> Aşağıdaki içerik doğrudan güncel `.cs` dosyalarından çıkarıldı.

## 1. Ne İşe Yarar

Müşteri tarafının üç giriş kapısı: yazılı senkron/akışlı (SSE) sohbet, gerçek zamanlı sesli
sohbet (WebSocket) ve oturum/geçmiş sorgulama. Üçü de **login olmuş bir müşteri** kimliğine
(`Customer` authorization policy + JWT `linked_customer_id` claim'i) bağlıdır.

## 2. Hangi Amaçla Kullanılır

- **ChatEndpoints:** Web/mobil istemcinin metin tabanlı sohbeti; hem "tek seferde JSON yanıt"
  hem "token token akan SSE" modları. Ayrıca engelleyici olmayan (non-blocking) HITL onay
  bildirimlerinin (bkz. [ApprovalGateService](../../CustomerSupportBot.Adapters.Agents/ApprovalGateService.md))
  müşteri tarafından okunduğu uçlar burada.
- **RealtimeEndpoints:** Tarayıcıdan mikrofonla konuşmak için iki farklı WebSocket modu
  (köprü/native — bkz. aşağıda).
- **SessionEndpoints:** Oturum listesi (sidebar), mesaj geçmişi ve debug amaçlı state görüntüleme.

## 3. Sorumlulukları

- HTTP/WebSocket yüzeyi + **oturum sahiplik kontrolü** (`SessionIdentityBinder.IsAccessibleAsync`) +
  girdi güvenliği (`IInputGuard.Inspect`, prompt injection/zararlı girdi taraması) + `IChatPort`'a
  delegasyon.
- `customerId` **HİÇBİR ZAMAN** istek gövdesinden alınmaz — her üç sınıf da JWT'deki
  `linked_customer_id` claim'ini okur (`ResolveAuthenticatedCustomerId`/`AuthenticatedCustomerId`).
  Bu, "kullanıcı başka birinin müşteri numarasını yazarak onun adına işlem yaptırabilir" açığını
  kapatan merkezi noktadır.
- `SessionEndpoints`, admin/agent'a **tüm** oturumları, müşteriye **yalnızca kendi** oturumlarını
  gösterecek şekilde kapsam daraltır (`TryResolveScope`) ve müşteriye dönen `SessionState`'ten
  admin-özel alanları (`ReplanNote`, `ReplanRequestedBy/At`) çıkarır (`CustomerVisibleState`).
- **Üstlenmediği:** reasoning/workflow yürütme mantığı (`IChatPort`/`ChatPortService`'te),
  gerçek ses köprüsü mantığı (`IRealtimeBridge`/`IRealtimeNativeBridge`'de).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IChatPort`, `ISessionPort`, `IHitlEventPort` — Application katmanı Inbound port'ları.
- `ISessionManager`, `IApprovalQueue` — Application katmanı Outbound port'ları (persistence).
- `SessionIdentityBinder` — oturum-kimlik eşleşmesini doğrulayan paylaşılan yardımcı
  (`Application.Services.Chat`); üç sınıf da AYNI kuralı kullanır, her biri kendi mantığını
  yazmaz.
- [SseWriter / SseForwarder](../Infrastructure/SseAndWebSockets.md) — SSE çerçeveleme.
- [WebSocketBrowserChannel](../Infrastructure/SseAndWebSockets.md) — PCM16 ses baytlarının
  WebSocket üzerinden taşınması, boyut sınırı (`MaxMessageBytes`).
- `IRealtimeBridge` (köprü modu) / `IRealtimeNativeBridge` (native mod) — `Adapters.AI`
  katmanında OpenAI Realtime API ile konuşan gerçek uygulamalar.
- [ChatEventOrchestrator](../Services/ChatEventOrchestrator.md) — `GET /chat/events/{sessionId}`
  kalıcı SSE bağlantısını yöneten orkestratör.
- `Program.cs` — bu üç sınıfın tüm uçları `RequireAuthorization("Customer")` (SessionEndpoints
  `"SessionAccess"` politikasını kullanır — Admin/Agent/Customer rollerinin hepsini kapsayacak
  şekilde tanımlı) ve `chat` rate-limit policy'sine (`ChatEndpoints`) tabidir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **Login zorunluluğu ve JWT'den customerId okuma, önceki "LLM'e customerId sor" modelinin
  yerini aldı** — sohbet metninden `customerId` çıkarmak, bir kullanıcının başka bir müşterinin
  numarasını yazarak onun siparişini görebilmesi/iptal edebilmesi anlamına geliyordu.
- **Oturum sahiplik kontrolü kimlik doğrulamadan AYRI bir katman:** "bu istek geçerli bir
  müşteriden mi" ile "bu oturum GERÇEKTEN bu müşteriye mi ait" farklı sorulardır — yalnızca
  birincisi kontrol edilseydi, müşteri B başka bir müşterinin `sessionId`'sini tahmin edip/bilip
  onun geçmişini okuyabilirdi. Henüz kimseye bağlanmamış bir oturuma erişim serbesttir (ilk
  temas oturumu çağırana bağlar).
- **`GET /sessions/{id}/state` müşteriye ham `SessionState`'i döndürmez:** `ReplanNote` gibi
  alanlar bilerek "admin/agent iç iletişimi, müşteri görmesin" diye tasarlanmıştı; ham nesneyi
  serileştirmek bunu ihlal ediyordu — düzeltme `CustomerVisibleState` projeksiyonuyla yapıldı.
  Bkz. [ContextPipeline.md](../../CustomerSupportBot.Application/Chat/ContextPipeline.md) ve
  ilgili commit geçmişi.
- **İki ayrı realtime modu (köprü / native) var:** köprü modda model yalnızca STT/TTS yapar,
  yanıtı agent pipeline üretir (tool onayı dahil tüm iş akışı kullanılabilir); native modda
  model doğrudan konuşur ve yalnızca **salt-okunur** tool'ları çağırabilir — yan etkili işlemler
  (sipariş oluşturma, iade) bu kanalda kasıtlı olarak YOKTUR (ses kanalında onay akışını
  yürütmek riskli/karmaşık olurdu).
- **Onay bildirimleri "unseen" (görülmemiş) ve "history" (tam geçmiş) olarak iki ayrı uca
  bölündü:** ilki badge sayacı için hafif bir sorgu, ikincisi salt-okunur tam liste — aynı veriyi
  farklı UI ihtiyaçları için iki kez sorgulamak yerine iki farklı projeksiyon sunulur.

## 6. Metotlar / Üyeler

### `ChatEndpoints` (tümü `RequireAuthorization("Customer")`)

| Route | Açıklama |
|---|---|
| `POST /chat/` *(`chat` rate-limit)* | Senkron sohbet: `ChatRequest` alır, `IChatPort.HandleAsync` ile reasoning+workflow tamamlanana kadar bekler, tek `ChatResponse` JSON döner. Girdi önce `IInputGuard.Inspect` ile taranır; reddedilirse `400 input_blocked`. |
| `POST /chat/stream` *(`chat` rate-limit)* | Aynı akış, SSE (`text/event-stream`) ile adım adım (`agent_started`, `tool_called`, `response_delta`, ...) yayınlanır. HITL olayları (`hitlEvents.Subscribe`) aynı akışa bindirilir. |
| `GET /chat/events/{sessionId}` | Kalıcı SSE bağlantısı — [ChatEventOrchestrator](../Services/ChatEventOrchestrator.md)'a delege eder; bot yanıtları + onay sonuçları + temsilci mesajlarının hepsini canlı yayınlar. |
| `GET /chat-sessions/{sessionId}/approvals/unseen` | Bağlı değilken kaçırılan onay sonuçlarını döner (badge/bildirim doldurma). |
| `POST /chat-sessions/{sessionId}/approvals/{id}/seen` | Bir onay bildirimini "görüldü" işaretler; hem oturum sahipliği hem onay kaydının `CustomerId`'si doğrulanır. |
| `GET /customer/approvals/history` | Müşterinin TÜM onay taleplerinin (bekleyen/onaylı/reddedilmiş) kalıcı geçmişi. |
| `ResolveAuthenticatedCustomerId(HttpContext)` *(private)* | JWT `linked_customer_id` claim'ini okur. |
| `IsSessionAccessibleAsync(...)` *(private)* | `SessionIdentityBinder.IsAccessibleAsync`'e delege eder. |

### `RealtimeEndpoints` (tümü `RequireAuthorization("Customer")`)

| Route | Açıklama |
|---|---|
| `ws:// /chat/realtime/{sessionId?}` | **Köprü modu.** OpenAI Realtime API yalnızca STT/TTS yapar; yanıtı agent pipeline üretir. `IRealtimeBridge.RunAsync`'e delege eder. |
| `ws:// /chat/realtime-native/{sessionId?}` | **Native modu.** Model kendisi konuşur, yalnızca salt-okunur tool'ları çağırabilir; yan etkili işlemler bu kanalda yoktur. `IRealtimeNativeBridge.RunAsync`'e delege eder. |
| `AuthenticatedCustomerId(HttpContext)` *(private)* | Yazılı chat ile aynı claim'i okur. |
| `CloseGracefullyAsync(WebSocket)` *(private)* | Bağlantıyı `NormalClosure` ile best-effort kapatır. |

### `SessionEndpoints` (tümü `RequireAuthorization("SessionAccess")`)

| Route | Açıklama |
|---|---|
| `GET /sessions/` | Oturumları listeler — müşteri yalnızca kendi oturumlarını, admin/agent tümünü görür (`TryResolveScope`). |
| `GET /sessions/{sessionId}/messages` | Oturumun mesaj geçmişi (`role`/`text` projeksiyonu). |
| `GET /sessions/{sessionId}/state` | Oturum meta verisi + state; müşteriye `CustomerVisibleState` projeksiyonu, admin/agent'a ham `SessionState`. |
| `TryResolveScope(HttpContext, out string?)` *(private)* | Admin/Agent → sınırsız (`null`); Customer → `linked_customer_id`; diğerleri → `false`. |
| `IsOwnSessionAsync(...)` *(private)* | `SessionIdentityBinder` ile aynı kuralı uygular. |
| `CustomerVisibleState(SessionState)` *(private)* | Admin-özel alanları (`ReplanNote` vb.) çıkaran projeksiyon. |

> **Not:** Bu sınıfta artık `DELETE /sessions/{id}` veya `/replan` uçları YOKTUR — session
> silme/replan işlevleri admin tarafına taşınmış durumda, bkz.
> [AdminAndHitl.md](AdminAndHitl.md) (`POST /chat-sessions/{sid}/replan`).

## 7. Bağımlılıklar

Constructor injection yok — her endpoint lambda'sı ilgili port'u (`IChatPort`, `IInputGuard`,
`ISessionManager`, `IHitlEventPort`, `ISessionPort`, `IApprovalQueue`, `IRealtimeBridge`,
`IRealtimeNativeBridge`) minimal API parametre injection'ı ile alır.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [Infrastructure/SseAndWebSockets](../Infrastructure/SseAndWebSockets.md)
- [Services/ChatEventOrchestrator](../Services/ChatEventOrchestrator.md)
