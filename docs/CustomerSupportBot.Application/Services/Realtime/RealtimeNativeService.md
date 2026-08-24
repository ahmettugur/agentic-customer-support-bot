# RealtimeNativeService

- **Kaynak:** `Services/Realtime/RealtimeNativeService.cs`
- **Tür:** `public sealed class : IRealtimeNativeBridge`
- **Namespace:** `CustomerSupportBot.Application.Services.Realtime`

## 1. Ne İşe Yarar

**Native mod** sesli görüşmenin orkestratörüdür: OpenAI'nin kendi modelinin doğrudan konuşup
karar vermesine izin verir (`create_response=true`), model yalnızca **okuma-only** tool'ları
(ürün sorgulama, sipariş/şikayet durumu) doğrudan çağırabilir. Sipariş oluşturma, iptal, iade,
şikayet kaydı gibi **yan etkili ve HITL gerektiren** tool'lar bu modda bilinçli olarak yoktur —
[`RealtimeBridgeService`](RealtimeBridgeService.md)'in aksine, burada cevabı model kendisi
üretir, bir agent-team pipeline'ı araya girmez.

## 2. Hangi Amaçla Kullanılır

Düşük gecikmeli, doğal sesli etkileşim isteyen ama hassas/yan-etkili işlemler gerektirmeyen
sorgular için (ürün bilgisi, sipariş durumu vb.) — model kendi başına, agent-team/reasoning
pipeline'ını beklemeden cevap üretebilir.

## 3. Sorumlulukları

**Üstlendiği:**
- Oturum kimliğini `SessionIdentityBinder.BindAtomicallyAsync` ile atomik bağlamak.
- Tarayıcı ↔ OpenAI ses/kontrol köprüsü.
- Model tool çağırdığında (`ToolCallReady`) çağrıyı **kendi içinde** (`DispatchTool`) işleyip
  sonucu OpenAI'ye geri vermek — burada agent-team/reasoning pipeline'ı YOKTUR.
- Hangi tool'ların sesli modda mevcut olduğuna karar vermek: okuma-only olanlar çalışır,
  yan-etkili olanlar `FORBIDDEN_IN_VOICE` ile reddedilir.
- `end_conversation` tool'unu özel olarak yakalayıp görüşmeyi kapatmak.
- 60 saniyelik kullanıcı hareketsizliği sonrası görüşmeyi otomatik sonlandırmak (`WatchInactivityAsync`).
- Sesli turu hem admin panelinin izleyebilmesi için `IChatBridge`'e hem ajan bağlamı için
  `ISessionManager`'a yazmak.

**Üstlenmediği:** Reasoning/routing (bu modda hiç çalışmaz), yan etkili işlemler (yazılı
sohbete veya bridge moduna yönlendirilir).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IRealtimeVoiceTransport` — OpenAI Realtime bağlantısı.
- `CustomerSupportToolsService` — doğrudan çağrılan okuma-only tool'ların kaynağı.
- `CustomerIdentityHintBuilder` — oturum talimatlarına eklenen kimlik/tarih metni (yazılı
  kanalla **aynı kaynaktan** üretilir).
- `IInputGuard` — transkript güvenlik denetimi.
- `IChatBridge`, `ISessionManager` — sesli turun kaydı.
- `IAppDistributedLock` — kimlik bağlama kilidi.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `customer_id` LLM argümanından neden asla okunmaz

`DispatchTool`, `authenticatedCustomerId` parametresini `session.State.AuthenticatedCustomerId`
(JWT'den) üzerinden alır — modelin ürettiği argümanlardan **asla** okunmaz. Aksi halde model
başka bir müşterinin sipariş geçmişini isteyebilirdi (impersonation açığı).

> 🐞 **Bu satır olmadan görülen hata:** Kimlik oturuma atomik bağlanmazsa
> `session.State.AuthenticatedCustomerId` boş kalıyordu ve sipariş tool'ları `customerId=""`
> ile koşup sahiplik kontrolüne takılıyordu — kullanıcıya "sipariş bulunamadı" olarak
> yansıyordu.

### Kullanıcı transkriptinin geçmişe yazılması neden gerekli

`_chatBridge.RecordBotExchange` yalnızca admin panelini besler; ajanın bağlamı
`ISessionManager`'dan gelir. Bu yazma olmadan sesli turlar konuşma geçmişinde hiç görünmüyordu
— bot moduna geçildiğinde ajan önceki isteği bilmiyordu, temsilci devraldığında panelde
müşterinin ne dediği görünmüyordu. Geçmişe "(sesli)" gibi bir placeholder yazmak yerine gerçek
transkript (`lastUserTranscript`) kullanılır.

### Asistan metninin buffer'lanması

`AssistantTextDelta`'lar, kullanıcı transkripti gönderilene kadar (`userTranscriptSent`)
`bufferedAssistantDeltas`'ta tutulur — transkript event'inden önce gelen delta'lar hemen
gönderilmez, transkript gönderildiğinde toplu olarak flush edilir. Bu, tarayıcı tarafında
asistan metninin kullanıcı balonundan önce görünmesini engeller.

### Inactivity timeout neden 60sn

Sesli bir bağlantı sonsuza kadar açık kalamaz — kullanıcı sessiz kaldığında (60sn) bağlantı
`idle_timeout` nedeniyle nazikçe kapatılır (`WatchInactivityAsync`), best-effort olarak hem
transport hem tarayıcı kanalı kapatılır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RunAsync(channel, sessionId, authenticatedCustomerId, ct)` | Bağlantının tüm ömrü: transport kontrolü → kimlik atomik bağlama → `_identityHint.BuildAsync` ile kimlik/tarih metni üretip `ConfigureNativeSessionAsync`'e verme → `PumpBrowserAsync`/`HandleEventsAsync`/`WatchInactivityAsync`'i paralel çalıştırma. |
| `WatchInactivityAsync(channel, ct)` *(private)* | Her 5 saniyede bir son kullanıcı aktivitesinden bu yana geçen süreyi kontrol eder; 60sn aşılırsa görüşmeyi `idle_timeout` nedeniyle sonlandırır. |
| `PumpBrowserAsync(channel, ct)` *(private)* | Tarayıcıdan gelen ses/kontrol mesajlarını işler; asistan konuşmuyorsa sesi OpenAI'ye iletir ve son aktivite zamanını günceller. |
| `HandleBrowserControlAsync(json, ct)` *(private)* | `interrupt`/`stop` kontrol mesajlarını işler. |
| `HandleEventsAsync(channel, session, ct)` *(private)* | OpenAI event akışının ana durumu: transkript geldiğinde guard kontrolü + geçmiş temizleme, asistan metin delta'larını buffer'lama/flush etme, `ToolCallReady` geldiğinde `pendingCalls`'a ekleme (`end_conversation` özel işlenir), `ResponseDone`'da bekleyen tool çağrıları varsa `DispatchToolCallsAsync`'i tetikleme, yoksa nihai metni geçmişe/chat-bridge'e yazıp `end_conversation` istenmişse bağlantıyı kapatma. |
| `DispatchToolCallsAsync(channel, calls, session, ct)` *(private)* | Bekleyen tüm tool çağrılarını paralel (`Task.WhenAll`) çalıştırır, sonuçları tarayıcıya bildirir ve `SendToolResultsAsync` ile OpenAI'ye geri verir (`triggerNextResponse: !_endRequested`). |
| `DispatchTool(name, argumentsJson, authenticatedCustomerId)` *(private)* | Tool adına göre `switch`: okuma-only tool'lar (`product_inquiry_tool`, `product_list_tool`, `order_status_tool`, `get_last_order_tool`, `get_all_orders_tool`) `CustomerSupportToolsService`'e delege edilir; `end_conversation` özel `ToolResult.Ok` döner; yan-etkili tool adları (`order_placement_tool`, `order_cancel_tool`, `return_request_tool`, `complaint_registration_tool`) `FORBIDDEN_IN_VOICE` ile reddedilir; bilinmeyenler `UNKNOWN_TOOL`. |

## 7. Bağımlılıklar

Constructor injection ile: `IRealtimeVoiceTransport`, `ISessionManager`,
`CustomerSupportToolsService`, `IInputGuard`, `IChatBridge`, `CustomerIdentityHintBuilder`,
`IAppDistributedLock`, `ILogger<RealtimeNativeService>`.

## Bağlantılar

- [RealtimeBridgeService.md](RealtimeBridgeService.md) — alternatif "köprü" modu (agent-team pipeline üretir)
- [../Providers/CustomerIdentityHintBuilder.md](../Providers/CustomerIdentityHintBuilder.md) — kimlik/tarih metninin ortak kaynağı
