# RealtimeNativeService

**Dosya:** `Services/Realtime/RealtimeNativeService.cs`
**Implements:** `IRealtimeNativeBridge`
**Uç:** `WS /chat/realtime-native/{sessionId?}` (UI'da "Hızlı Sesli")

> ⚠️ Bu doküman eskiden bu sınıfı "SignalR üzerinden real-time mesaj push" olarak anlatıyordu.
> Yanlıştı — sınıfın SignalR ile hiçbir ilgisi yok.

## 1. Ne İşe Yarar

**Native sesli mod**: OpenAI Realtime modeli kullanıcıyla doğrudan konuşur ve gerekirse
salt-okunur tool'ları kendisi çağırır. Bu servis tarayıcı ses kanalı (`IBrowserChannel`) ile
OpenAI ses taşıyıcısı (`IRealtimeVoiceTransport`) arasındaki orkestrasyondur.

## 2. Köprü modundan farkı

| | Native (`bu sınıf`) | Köprü ([RealtimeBridgeService](RealtimeBridgeService.md)) |
|---|---|---|
| Cevabı kim üretir | OpenAI Realtime modeli | Ajan pipeline'ı (reasoning + workflow) |
| Reasoning | **Yok** | Var (`IReasoningPort`) |
| Tool seti | Salt-okunur 5 tool + `end_conversation` | Pipeline'ın tamamı |
| Gecikme | Düşük | Yüksek (pipeline süresi kadar) |

> 🔎 **Teşhis için önemli:** Native modda `EntityVerifier` / `IdExtractor` **akışın içinde
> değildir**. `order_id` doğrudan modelin ürettiği function-call argümanından gelir
> (`DispatchTool`). Bu iki sınıf yalnızca köprü modunda devreye girer.

## 3. Sorumlulukları

- Ses akışını iki yönde pompalamak (`PumpBrowserAsync`, `HandleEventsAsync`).
- Half-duplex kapı: asistan konuşurken mikrofon sesi OpenAI'ye iletilmez.
- Tool dispatch (`DispatchTool`) — hangi tool'un sesli modda çalışabileceği burada kapsüllenir.
- Hareketsizlik zaman aşımı (60sn) ve `end_conversation` ile görüşmeyi kapatmak.
- **Oturuma kimlik bağlamak** — aşağıya bakın.

## 4. Kimlik: sesli kanalın en kritik satırı

```csharp
if (!await SessionIdentityBinder.TryBindAsync(session, authenticatedCustomerId, _sessionManager, ct))
    // → bağlantı reddedilir
```

Sipariş tool'ları `customerId`'yi **yalnızca** `session.State.AuthenticatedCustomerId`'den alır;
LLM'e `customer_id` diye bir parametre hiç açılmaz. Dolayısıyla bu bağ kurulmazsa tool'lar
`customerId=""` ile koşar.

> 🐞 **Canlı hata:** Bu bağ uzun süre hiç kurulmuyordu (WebSocket ucu JWT'yi okumuyordu bile).
> Sonuç: sipariş sorguları `ValidateOrderActionable` sahiplik kontrolüne takılıyor ve — "yok"
> ile "başkasının" ayrımı kasıtlı gizlendiği için — kullanıcı **"sipariş bulunamadı"** duyuyordu.
> Ürün sorguları `customerId` almadığından çalışmaya devam ettiği için arıza tool dispatch'i
> gibi görünmüyordu. Yazılı sohbette aynı `sessionId` ile bir tur geçmişse kimlik orada
> bağlandığından sesli kanal *bazen* çalışıyordu; teşhisi zorlaştıran buydu.

Aynı çağrı sahipliği de doğrular: oturum başka bir müşteriye bağlıysa bağlantı reddedilir.
Bkz. [security.md](../../security.md#oturum--müşteri-bağı).

## 4.5. Turun geçmişe yazılması

Native turun kullanıcı tarafı **gerçek transkript** ile — sabit bir `"(sesli)"` etiketiyle
DEĞİL — hem `_chatBridge.RecordBotExchange` (admin paneli) hem `_sessionManager.AddExchangeAsync`
(ajanın konuşma bağlamı) üzerinden yazılır.

> 🐞 **Canlı hata (kısmen düzeltildi):** Bu bağ eskiden `"(sesli)"` sabit metniyle kurulup DB
> geçmişine hiç yazılmıyordu — chat bridge'e bile yalnızca `RecordBotExchange` çağrısı gidiyordu,
> `AddExchangeAsync` hiç çağrılmıyordu. Sonuç: bot moduna geçildiğinde ajan önceki sesli isteği
> **bilmiyordu**, admin paneli de müşterinin ne söylediğini göremiyordu. `lastUserTranscript`
> — `InputTranscriptCompleted` event'inden geleni tutan yerel bir değişken — artık `finalText`
> ile birlikte hem bridge'e hem session yöneticisine geçiyor.
>
> **Kapatılmayan yarı:** `create_response=true` olduğu için model, transkript denetiminden
> (`_inputGuard.Inspect`) ÖNCE ses veya read-only tool üretmeye başlayabiliyor. Guard reddettiğinde
> zaten `SendInterruptAsync` ile kesiliyor ama bu bir tasarım kararı gerektiriyor:
> `create_response=false` + her turda manuel `response.create` demek, native modun düşük
> gecikme karakterini değiştirir.

## 5. Yasak tool'lar

`order_placement_tool`, `order_cancel_tool`, `return_request_tool`,
`complaint_registration_tool` sesli modda **tanımlanmaz** — model şemalarını bile görmez.
Yine de bir şekilde çağrılırsa `DispatchTool` `FORBIDDEN_IN_VOICE` döner ve kullanıcı yazılı
sohbete yönlendirilir. Sebep: HITL onayı ses akışını bloklar.

## Bağlantılar

- [RealtimeBridgeService.md](RealtimeBridgeService.md) — Diğer sesli mod
- [../../CustomerSupportBot.Adapters.AI/Realtime.md](../../CustomerSupportBot.Adapters.AI/Realtime.md) — Taşıyıcı, tool şemaları, kimlik doğrulama
- [../Tools/OrderToolsService.md](../Tools/OrderToolsService.md) — Çağrılan sipariş tool'ları
- [../../security.md](../../security.md#oturum--müşteri-bağı) — Oturum sahipliği
