# RealtimeNativeService

- **Kaynak:** `Services/Realtime/RealtimeNativeService.cs`
- **Tür:** `public sealed class : IRealtimeNativeBridge`
- **Namespace:** `CustomerSupportBot.Application.Services.Realtime`

## 1. Ne İşe Yarar

**Native mod** sesli görüşmenin orkestratörüdür: OpenAI'nin kendi modelinin doğrudan konuşup
karar vermesine izin verir (`create_response=true`); cevabı model kendisi üretir, bir agent-team
pipeline'ı araya girmez. Model yazılı sohbetin iş tool'larının **tamamını** çağırabilir: okuma
tool'ları hemen çalışır; **yan etkili** olanlar (sipariş oluşturma, iptal, iade, şikayet kaydı)
işlemi yapmaz, yazılı sohbetle aynı [`SideEffectApprovalGate`](../Approval/SideEffectApprovalGate.md)
üzerinden insan onayına kayıt açar.

> Uygulamadaki **tek sesli moddur.** Eskiden ayrıca yanıtı agent-team pipeline'ına ürettiren bir
> "köprü" modu (`RealtimeBridgeService`, `/chat/realtime`) vardı; kaldırıldı.

## 2. Hangi Amaçla Kullanılır

Düşük gecikmeli, doğal sesli etkileşim için — model kendi başına, agent-team/reasoning
pipeline'ını beklemeden cevap üretir. Sorgular, işlem talepleri (onaya gider) ve temsilciye
yönlendirme sesle yapılabilir.

## 3. Sorumlulukları

**Üstlendiği:**
- Oturum kimliğini `SessionIdentityBinder.BindAtomicallyAsync` ile atomik bağlamak.
- Tarayıcı ↔ OpenAI ses/kontrol köprüsü.
- Model tool çağırdığında (`ToolCallReady`) çağrıyı **kendi içinde** (`DispatchToolAsync`) işleyip
  sonucu OpenAI'ye geri vermek — burada agent-team/reasoning pipeline'ı YOKTUR.
- Yan etkili tool'ları onay kapısına yönlendirmek (`RunSideEffectToolAsync`): parametre
  anahtarları yazılı kanalla aynıdır (aynı yürütücü okur, aynı talebin iki kanaldan gelen kopyası
  tek onay kaydında birleşir); onay kaydı oluşturulmadan önce ön kontrol yapılır (sipariş var mı,
  bu müşteriye mi ait; sipariş satırları eksiksiz mi).
- `human_handoff_tool` ile müşteri temsilci istediğinde temsilci kuyruğuna (eskalasyon) talep açmak.
- `end_conversation` tool'unu özel olarak yakalayıp görüşmeyi kapatmak.
- 60 saniyelik kullanıcı hareketsizliği sonrası görüşmeyi otomatik sonlandırmak (`WatchInactivityAsync`).
- Sesli turu hem admin panelinin izleyebilmesi için `IChatBridge`'e hem ajan bağlamı için
  `ISessionManager`'a yazmak.
- Tur kaydından sonra duygu durumunu değerlendirmek (`sentiment_update` / `sentiment_alert`
  olayları) ve uyarı eşiği aşıldığında eskalasyon açmak (`EvaluateSentimentAsync`).

**Üstlenmediği:** Reasoning/routing (bu modda hiç çalışmaz), yan etkili işlemin kendisi
(admin onayından sonra `IApprovalExecutionRouter` yürütür).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IRealtimeVoiceTransport` — OpenAI Realtime bağlantısı.
- `CustomerSupportToolsService` — tool'ların iş mantığı (yazılı sohbetle aynı).
- [`SideEffectApprovalGate`](../Approval/SideEffectApprovalGate.md) — yan etkili tool'ların onay kapısı.
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

`_chatBridge.RecordBotExchangeAsync` yalnızca admin panelini besler; ajanın bağlamı
`ISessionManager`'dan gelir. Bu yazma olmadan sesli turlar konuşma geçmişinde hiç görünmüyordu
— bot moduna geçildiğinde ajan önceki isteği bilmiyordu, temsilci devraldığında panelde
müşterinin ne dediği görünmüyordu. Geçmişe "(sesli)" gibi bir placeholder yazmak yerine gerçek
transkript kullanılır.

### Transkript ↔ yanıt eşleştirmesi (`VoiceTurnPairer`)

OpenAI Realtime'da kullanıcı sesinin transkripsiyonu yanıt üretimiyle paralel çalışır; transkript
yanıt olaylarından önce, ortasında ya da `response.done`'dan sonra gelebilir. Servis eskiden yanıtı
"o ana kadarki son transkript"le eşliyordu: transkript geç geldiğinde tur bir önceki kullanıcı
cümlesiyle (ya da `(sesli)` ile) kaydediliyor, kural tabanlı duygu çıkarımı da yanlış metin
üzerinde çalışıyordu. Ayrıca transkript yanıtın ortasında gelirse o ana kadarki asistan metni
kayıttan siliniyordu (`"Siparişiniz kargoda."` → `"kargoda."`).

Artık eşleştirme protokolün kendi bağıyla yapılır ([VoiceTurnPairer](VoiceTurnPairer.md)):
transkript ait olduğu kullanıcı ses öğesinin `item_id`'sini taşır; bir yanıt, başladığı anda son
commit edilmiş (`input_audio_buffer.committed`) kullanıcı öğesine aittir; tool sonucu üzerine gelen
takip yanıtı aynı tura aittir. Turlar geçmişe konuşma sırasıyla yazılır. Transkripsiyon başarısız
olursa (`…transcription.failed` ya da boş transkript) tur yer tutucuyla yazılır; girdi
güvenliğince reddedilen transkriptin turu hiç yazılmaz. Bağlantı kapanırken transkripti gelmemiş
turlar ve tool çağrısıyla biten son yanıtın metni (ör. `end_conversation` ile söylenen veda) de
kaydedilir. Kimlik taşımayan bir olay akışında eski davranış (son transkript) korunur.

Tool sonrası takip yanıtı, transkript zaten gönderildiyse tarayıcıya akmaya devam eder ve tool
öncesi söylenen cümlenin tamponlanmış metni atılmaz (eskiden ekranda hiç çıkmıyordu).

### Asistan metninin buffer'lanması

`AssistantTextDelta`'lar, kullanıcı transkripti gönderilene kadar (`userTranscriptSent`)
`bufferedAssistantDeltas`'ta tutulur — transkript event'inden önce gelen delta'lar hemen
gönderilmez, transkript gönderildiğinde toplu olarak flush edilir. Bu, tarayıcı tarafında
asistan metninin kullanıcı balonundan önce görünmesini engeller.

### Duygu uyarısı ve eskalasyon — metin kanalıyla eşdeğerlik

Metin kanalında tur sonunda duygu uyarısı değerlendirilir (bkz.
[ChatPortService](../Chat/ChatPortService.md)) ve eskalasyon, workflow'daki uzman ajanın
`needs_escalation` kararından gelir ([EscalationPolicyService](../Escalation/EscalationPolicyService.md)).
Native modda workflow yoktur; bu yüzden ne uyarı değerlendiriliyor ne de eskalasyona giden
**hiçbir yol** vardı — art arda olumsuz turlar yaşayan sesli bir müşteri yazılı kanala geçmedikçe
insana ulaşamıyordu.

Artık her kaydedilen turdan sonra (duygu, `AddExchangeAsync`'in kural tabanlı çıkarımıyla
güncellenir — bu modda LLM `TurnSignals`'ı yok) `SessionStateService.CheckSentimentAlert` çağrılır,
tarayıcıya metin kanalıyla aynı `sentiment_update` / `sentiment_alert` olayları gönderilir. Bu
kanalda elimizdeki tek sinyal ardışık olumsuz duygu olduğu için uyarı eşiği
(`AutoEscalationConsecutiveNegative`) aşıldığında `RealtimeVoiceAgent` adına, `High` öncelikli bir
eskalasyon açılır. Sink'in session + ajan dedup'ı oturum başına tek açık kayıt tutar; eşik sonraki
turlarda yeniden aşılsa da ikinci kayıt oluşmaz. Değerlendirme/eskalasyon hatası görüşmeyi
kesmez (loglanır).

Duygu değerlendirmesi her tur [eşleştirilip](#transkript--yanıt-eşleştirmesi-voiceturnpairer)
geçmişe yazıldıktan sonra çalışır, yani doğru kullanıcı cümlesi üzerinde yapılır.

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
| `DispatchToolCallsAsync(channel, calls, session, userQuery, ct)` *(private)* | Bekleyen tüm tool çağrılarını paralel (`Task.WhenAll`) çalıştırır, sonuçları tarayıcıya bildirir ve `SendToolResultsAsync` ile OpenAI'ye geri verir (`triggerNextResponse: !_endRequested`). `userQuery`, bu turun kullanıcı cümlesidir (onay kaydı/eskalasyon için; transkript henüz gelmediyse yer tutucu). |
| `DispatchToolAsync(name, argumentsJson, session, userQuery)` *(internal)* | Tool adına göre yönlendirir: okuma tool'ları (ürün, sipariş, şikayet sorguları) `CustomerSupportToolsService`'e; yan etkili tool'lar `RunSideEffectToolAsync`'e; `human_handoff_tool` `RequestHumanHandoffAsync`'e; `end_conversation` özel `ToolResult.Ok`; bilinmeyenler `UNKNOWN_TOOL`. Müşteri kimliği her zaman oturumdaki doğrulanmış kimliktir, model argümanından okunmaz. Sayısal argümanlar (ör. `order_id: 1030`) da kabul edilir. |
| `RunSideEffectToolAsync(...)` *(private)* | Sipariş/iptal/iade/şikayet kaydı → `SideEffectApprovalGate.ExecuteAsync` (ön kontrol + onay kaydı + "onaya gönderildi"). Kapı yoksa `APPROVAL_UNAVAILABLE` — yan etkili işlem asla doğrudan çalışmaz. |
| `RequestHumanHandoffAsync(...)` *(private)* | `HumanHandoffAgent` adına eskalasyon açar; sohbet sayfası bunu `handoff_pending` olarak görür. |

## 7. Bağımlılıklar

Constructor injection ile: `IRealtimeVoiceTransport`, `ISessionManager`,
`CustomerSupportToolsService`, `IInputGuard`, `IChatBridge`, `CustomerIdentityHintBuilder`,
`IAppDistributedLock`, `ILogger<RealtimeNativeService>`, ve opsiyonel olarak
`SessionStateService?` (duygu uyarısı), `IEscalationSink?` (eskalasyon, temsilci talebi) ve
`SideEffectApprovalGate?` (yan etkili tool'lar) — verilmezse ilgili adım atlanır; onay kapısı
yoksa yan etkili tool'lar reddedilir.

## Bağlantılar

- [../Providers/CustomerIdentityHintBuilder.md](../Providers/CustomerIdentityHintBuilder.md) — kimlik/tarih metninin ortak kaynağı
