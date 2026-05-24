# Realtime Sesli Konuşma Servisleri

**Dosyalar:**  
- `Services/RealtimeBridgeService.cs` → `IRealtimeBridge`  
- `Services/RealtimeNativeService.cs` → `IRealtimeNativeBridge`  

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
  Browser Binary → _assistantSpeaking == false? → SendAudioChunkAsync
  Browser Text   → interrupt / stop

HandleEventsAsync (Task)
  ResponseCreated       → _assistantSpeaking = true
  SpeechStarted/Stopped → browser'a ilet
  InputTranscriptCompleted →
    InputGuard.Inspect(transcript)
      Reject → SpeakTextAsync(hata mesajı) + erken çık
      Allow  → HandleUserTranscriptAsync (fire-and-forget)
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

`_assistantSpeaking` (volatile bool) ile tarayıcıdan gelen ses, asistan konuşurken OpenAI'ye iletilmez. Bu sayede asistan kendi sesini duyarak döngüye girmez.

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

### Tool dispatch

`DispatchTool(name, argumentsJson)` metodu:

| Tool | Aksiyon |
|------|--------|
| `product_inquiry_tool` | `_tools.ProductInquiryTool(...)` |
| `order_status_tool` | `_tools.OrderStatusTool(...)` |
| `get_last_order_tool` | `_tools.GetLastOrderTool(...)` |
| `get_all_orders_tool` | `_tools.GetAllOrdersTool(...)` |
| `end_conversation` | `_endRequested = true`, sonlandırma sinyali |
| `order_placement_tool` | **FORBIDDEN** — `FORBIDDEN_IN_VOICE` hatası |
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
