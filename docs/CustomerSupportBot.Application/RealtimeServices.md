# Realtime Sesli Konuşma Servisleri

**Dosyalar:**  
- `Services/Realtime/RealtimeBridgeService.cs` → `IRealtimeBridge`  
- `Services/Realtime/RealtimeNativeService.cs` → `IRealtimeNativeBridge`  

**Yaşam döngüsü:** **Scoped** (her WebSocket/SSE bağlantısı için ayrı instance — diğer tüm servisler Singleton)

## İki mod

| Mod | Servis | Açıklama |
|-----|--------|---------|
| **Bridge** | `RealtimeBridgeService` | Ses → transkript → tam agent pipeline (Reasoning + MAF workflow) |
| **Native** | `RealtimeNativeService` | Ses → model kendi karar verir + read-only tool çağrısı (Reasoning/MAF yok) |

---

## RealtimeBridgeService

### Ne yapar?

Tarayıcıdan gelen ham ses verisini OpenAI Realtime API'ye iletir, transkripsiyonu alır, ardından tam bot pipeline'ını (InputGuard → Reasoning → AgentTeam) çalıştırır. Sonucu `IRealtimeVoiceTransport.SpeakTextAsync` ile sese çevirir.

### Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IRealtimeVoiceTransport` | OpenAI Realtime API adaptörü |
| `IAgentTeamPort` | MAF workflow (Reasoning + Agents) |
| `ISessionManager` | Session okuma/yazma |
| `IReasoningPort` | Reasoning pipeline |
| `IApprovalContextAccessor` | HITL scope |
| `IChatBridge` | Konuşmayı kayıt için bridge |
| `IInputGuard` | Gelen transkript güvenlik filtresi |

### `RunAsync` akışı

```
IBrowserChannel ←→ RealtimeBridgeService ←→ IRealtimeVoiceTransport (OpenAI)

PumpBrowserAsync (Task)
  Browser Binary → IsBusy == false? → SendAudioChunkAsync
                   (IsBusy = _assistantSpeaking || _turnInFlight)
  Browser Text   → interrupt / stop

HandleEventsAsync (Task)
  ResponseCreated       → _assistantSpeaking = true
  SpeechStarted/Stopped → browser'a ilet
  InputTranscriptCompleted →
    _turnInFlight ? → yok say (log) ve çık
    browser'a user_transcript gönder
    _turnInFlight = true
    InputGuard.Inspect(transcript)   [HandleUserTranscriptAsync içinde]
      Reject → SpeakTextAsync(hata mesajı) + erken çık
      Allow  → reasoning + workflow
    finally → _turnInFlight = false
  AudioDelta            → browser'a binary gönder
  AssistantTextDelta    → browser'a text gönder
  ResponseDone/Cancelled → _assistantSpeaking = false
  ConnectionClosed      → çık
```

### `HandleUserTranscriptAsync`

Full bot pipeline — chat ile aynı mantık:
```
1. InputGuard.Inspect(transcript)
2. ReasoningPort.ReasonStreamingAsync → forward to browser
3. AgentTeamPort.RunStreamingAsync    → forward to browser
4. SessionManager.AddExchange(...)
5. IRealtimeVoiceTransport.SpeakTextAsync(responseText)
   prompt: "Yukarıdaki metni Türkçe olarak doğal, samimi bir tonla harfiyen oku."
```

### Half-duplex gating

Tarayıcıdan gelen ses **`IsBusy`** iken OpenAI'ye iletilmez. `IsBusy` iki bayrağın birleşimidir:

| Bayrak | Kapattığı pencere |
|---|---|
| `_assistantSpeaking` | Asistan konuşurken — kendi sesini duyup döngüye girmesini engeller |
| `_turnInFlight` | Transkript alındıktan sonra agent pipeline (reasoning + workflow) sürerken |

> ⚠️ **`_turnInFlight` neden gerekli:** bridge modunda `create_response=false` olduğu için asistan ancak pipeline bitince `SpeakTextAsync` ile konuşur. Yani "kullanıcı sustu" ile "asistan konuşuyor" arasında **saniyeler süren sessiz bir aralık** vardır ve bu aralıkta `_assistantSpeaking` hâlâ `false`'tur.
>
> Bu aralık korumasızken canlıda şu hata görüldü: mikrofon akmaya devam ediyor, `semantic_vad` sessizlik/gürültüde tetikleniyor ve transkripsiyon modeli — `AiProviderOptions.TranscriptionPrompt` ile domain sözlüğüne yönlendirildiği için — boş dönmek yerine makul görünen bir cümle uyduruyordu (*"Merhaba, müşteri numaram 1025."*; prompt'ta örnek olarak verilen 1008/1027'nin komşusu). Sahte transkript hem sohbete kullanıcı balonu olarak düşüyor hem de **ikinci bir pipeline** başlatıp ilk turun event akışıyla karışıyordu — ilk turun `reasoning_complete`'i kaybolduğu için paneli yarım JSON'da kilitli kalıyordu.
>
> Üç katmanlı düzeltme: (1) `TranscriptionPrompt`'tan tohumlayıcı örnek numaralar çıkarıldı, (2) `_turnInFlight` mikrofonu pipeline boyunca susturuyor, (3) buna rağmen gecikmeli gelen transkript `HandleEventsAsync`'te düşürülüyor (eşzamanlı tur imkânsız).

> `_turnInFlight`, `HandleUserTranscriptAsync`'in **`finally`** bloğunda sıfırlanır — başarı, guard reddi, iptal ve hata yollarının hepsinde. Sıfırlanmazsa mikrofon kalıcı susar ve oturum sağır olur. Aynı sebeple bayrak `user_transcript` gönderimiNDEN SONRA set edilir: gönderim fırlarsa `finally`'e hiç girilmeyeceği için bayrak kilitlenirdi.

`RealtimeNativeService`'te bu ek bayrak **yoktur ve gerekmez** — orada `create_response=true` olduğu için model konuşmaya hemen başlar, sessiz aralık oluşmaz.

---

## RealtimeNativeService

### Ne yapar?

Tam agent pipeline olmaksızın OpenAI Realtime modelinin kendi araç çağrısı kararlarını almasına izin verir. Sadece read-only tool'lar kullanılabilir; HITL gerektiren tool'lar (sipariş verme, şikayet açma) kasıtlı olarak engellenir.

### Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IRealtimeVoiceTransport` | OpenAI Realtime API |
| `ISessionManager` | Session yönetimi |
| `CustomerSupportToolsService` | Tool implementasyonları (concrete tip — session yok) |
| `IInputGuard` | Transkript filtresi |
| `IChatBridge` | Konuşma kayıt |

### `RunAsync` akışı

Bridge moduna ek olarak üçüncü bir Task çalışır:

```
PumpBrowserAsync     → ses aktarımı (aynı mantık)
HandleEventsAsync    → event işleme + tool dispatch
WatchInactivityAsync → 60 saniye sessizlik → konuşmayı sonlandır
```

### Tool şemaları — `RealtimeFunctionTools`

**Dosya:** `CustomerSupportBot.Adapters.AI/Realtime/RealtimeFunctionTools.cs`

`session.update` payload'ında OpenAI'ye gönderilen JSON Schema tanımları. Model yalnızca burada tanımlı tool'ları görebilir ve çağırabilir — tanımsız tool'lar modele görünmez.

| Tool | Zorunlu parametre | Opsiyonel parametre |
|------|-------------------|---------------------|
| `product_inquiry_tool` | `product_name` | — |
| `product_list_tool` | — | `category` |
| `order_status_tool` | `order_id` | — |
| `get_last_order_tool` | `customer_id` | — |
| `get_all_orders_tool` | `customer_id` | — |
| `end_conversation` | — | `reason` |

HITL gerektiren tool'lar (`order_placement_tool`, `order_cancel_tool`, `return_request_tool`, `complaint_registration_tool`) şema listesine **dahil edilmez** — model bu tool'ların varlığından haberdar olmaz, dolayısıyla çağıramaz.

### Tool dispatch

`DispatchTool(name, argumentsJson)` metodu:

| Tool | Aksiyon |
|------|--------|
| `product_inquiry_tool` | `_tools.ProductInquiryTool(...)` |
| `product_list_tool` | `_tools.ProductListTool(...)` |
| `order_status_tool` | `_tools.OrderStatusTool(...)` |
| `get_last_order_tool` | `_tools.GetLastOrderTool(...)` |
| `get_all_orders_tool` | `_tools.GetAllOrdersTool(...)` |
| `end_conversation` | `_endRequested = true`, sonlandırma sinyali |
| `order_placement_tool` | **FORBIDDEN** — `FORBIDDEN_IN_VOICE` hatası |
| `order_cancel_tool` | **FORBIDDEN** — `FORBIDDEN_IN_VOICE` hatası |
| `return_request_tool` | **FORBIDDEN** — `FORBIDDEN_IN_VOICE` hatası |
| `complaint_registration_tool` | **FORBIDDEN** — `FORBIDDEN_IN_VOICE` hatası |

Tool sonuçları `IRealtimeVoiceTransport.SendToolResultsAsync` ile modele iletilir; model yanıtlamaya devam eder.

### Inactivity timeout

```
WatchInactivityAsync:
  Her 5 saniyede kontrol
  Son kullanıcı aktivitesinden bu yana > 60 saniye?
    → _endRequested = true, _endReason = "idle_timeout"
    → browser'a conversation_ended event'i
    → bağlantı kapatılır
```

`_lastUserActivityTicks` ses chunk geldiğinde `Interlocked.Exchange` ile güncellenir.

### `end_conversation` tool'u

Model vedalaşma tespitinde bu tool'u çağırır. `reason` parametresini ayarlar; `ResponseDone` event'i gelince bağlantı temiz şekilde kapatılır.

---

## Bridge vs Native karşılaştırma

| Özellik | Bridge | Native |
|---------|--------|--------|
| Reasoning pipeline | Var (tam) | Yok |
| MAF agent workflow | Var (tam) | Yok |
| Tool çağrısı | Reasoning sonrası tool'lar | Model kendi karar verir |
| HITL (sipariş/şikayet) | Var | Yok (kasıtlı engel) |
| İnactivity timeout | Yok | 60 saniye |
| end_conversation tool | Yok | Var |
| Konuşma geçmişi | SessionManager'a yazılır | ChatBridge'e kayıt |

---

## Neden Scoped?

Her WebSocket bağlantısı için `_assistantSpeaking`, `_endRequested`, `_endReason` ve `_lastUserActivityTicks` gibi bağlantıya özel volatile state tutulur. Singleton olsaydı bu state'ler farklı bağlantılar arasında karışırdı.
