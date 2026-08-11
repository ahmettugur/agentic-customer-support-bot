# Realtime — OpenAI Voice Bridge

**Dosyalar:**

- `Realtime/OpenAiRealtimeClientAdapter.cs` — WebSocket köprüsü
- `Realtime/RealtimeFunctionTools.cs` — Read-only tool schema kataloğu

## 1. Ne İşe Yarar

OpenAI Realtime API kullanarak **iki yönlü ses iletişimi** sağlar: kullanıcı konuşur → bot anlar → sesli yanıt verir.

## 2. Hangi Amaçla Kullanılır

Chat ekranında "sesli sohbet" modu — kullanıcı mikrofon açıp konuşabilir, bot sesli yanıt verir.

> 💡 **Analiz notu:** Alexa veya Siri gibi — konuşarak müşteri desteği. Arka planda iki WebSocket var: biri browser↔sunucu, diğeri sunucu↔OpenAI. Ses verisi (PCM 24kHz) iki taraflı köprüden geçer.

---

## Mimari genel bakış

```
Browser                              Api Layer                 OpenAI Realtime
─────────                            ─────────                 ──────────────
  Mikrofon (PCM 24kHz)
       ↓ WebSocket
   Api/Realtime endpoint
       ↓ DI: IRealtimeVoiceTransport (Scoped)
   OpenAiRealtimeClientAdapter
       ↓ ClientWebSocket
   wss://api.openai.com/v1/realtime?model=...
       ↓
       ↓ Authorization: Bearer ...
       ↓
       ↓ session.update (config)
       ↓ input_audio_buffer.append (mikrofon)
       ↓
       ↑ response.output_audio.delta (ses)
       ↑ response.output_audio_transcript.delta (text)
       ↑ response.function_call_arguments.done (tool çağrısı)
```

Application katmanı (`RealtimeBridgeService` veya `RealtimeNativeService`) bu transport'u kullanarak iki mod sunar:

- **Bridge mode:** Reasoning + AgentTeam → text üret → Realtime'a "şunu oku" der
- **Native mode:** OpenAI doğrudan tool çağırır, kendi yanıt verir

---

## `IRealtimeVoiceTransport` arayüzü

Adapter aşağıdaki metodları implement eder:

| Metod | Açıklama |
| --- | --- |
| `TryConnectAsync` | WebSocket bağlantısını aç |
| `ConfigureBridgeSessionAsync` | Bridge modu config (auto-response = false) |
| `ConfigureNativeSessionAsync` | Native modu config (tools + auto-response = true) |
| `SendAudioChunkAsync` | Browser'dan gelen PCM'i OpenAI'a forward |
| `SendInterruptAsync` | Asistan konuşmasını kes |
| `SpeakTextAsync` | Belirli bir metni okut (Bridge mode) |
| `SendToolResultsAsync` | Tool sonucu döndür (Native mode) |
| `ReceiveEventsAsync` | IAsyncEnumerable — OpenAI'dan gelen event akışı |
| `CloseAsync` | WebSocket'i graceful kapat |

### Property'ler

```csharp
public bool IsEnabled { get; }              // RealtimeOptions.Enabled
public string ModelName { get; }            // "gpt-realtime-2"
public string Voice { get; }                // "alloy", "echo", ...
public IReadOnlyList<string> NativeToolNames { get; }
```

---

## Bağlantı kurulumu

```csharp
public async Task<bool> TryConnectAsync(CancellationToken ct)
{
    _ws = new ClientWebSocket();
    _ws.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
    var url = OpenAiRealtimeUrl + Uri.EscapeDataString(_options.Model);
    await _ws.ConnectAsync(new Uri(url), ct);
    return true;
}
```

- `_apiKey`: `RealtimeOptions.ApiKey` dolu ise onu, boş ise `OpenAiOptions.ApiKey`'i kullanır
- `OpenAiRealtimeUrl = "wss://api.openai.com/v1/realtime?model="`
- Başarısızlık → `false` döner (exception log'a yazılır); caller fallback davranışı uygular

---

## İki mod: Bridge vs Native

### Bridge mode (`ConfigureBridgeSessionAsync`)

```json
{
  "type": "session.update",
  "session": {
    "output_modalities": ["audio"],
    "audio": {
      "input": {
        "format": { "type": "audio/pcm", "rate": 24000 },
        "transcription": { "model": "gpt-4o-transcribe", "language": "tr" },
        "turn_detection": {
          "type": "semantic_vad",
          "eagerness": "medium",
          "create_response": false,    // ← AUTO-RESPONSE KAPALI
          "interrupt_response": true
        }
      },
      "output": { "format": { "type": "audio/pcm", "rate": 24000 }, "voice": "alloy" }
    },
    "instructions": "Sağlanan metni Türkçe doğal sesle oku..."
  }
}
```

**Akış:**

1. Kullanıcı konuşur → transkripsiyon → Application'a teslim edilir
2. Application **kendi pipeline'ında** yanıt üretir (Reasoning + Specialist + Response agents)
3. `SpeakTextAsync(yanıt)` çağrılır → Realtime sadece TTS yapar

OpenAI bu modda kendiliğinden cevap vermez (`create_response=false`).

### Native mode (`ConfigureNativeSessionAsync`)

```json
{
  "type": "session.update",
  "session": {
    "output_modalities": ["audio"],
    "audio": {
      "input": {
        "format": { "type": "audio/pcm", "rate": 24000 },
        "transcription": { "model": "gpt-4o-transcribe", "language": "tr" },
        "turn_detection": {
          "type": "semantic_vad",
          "eagerness": "medium",
          "create_response": true,     // ← OTOMATIK YANIT
          "interrupt_response": true
        }
      },
      "output": { "format": { "type": "audio/pcm", "rate": 24000 }, "voice": "alloy" }
    },
    "tools": [ /* read-only tools */ ],
    "tool_choice": "auto",
    "instructions": "Müşteri destek asistanı..."
  }
}
```

**Akış:**

1. Kullanıcı konuşur → OpenAI direkt anlar
2. Gerekirse tool çağırır (ör. `order_status_tool`)
3. `response.function_call_arguments.done` event'i gelir → Application dispatch eder → sonuç `SendToolResultsAsync` ile döner
4. OpenAI ses yanıtı verir

Avantaj: Düşük latency, daha doğal konuşma  
Dezavantaj: HITL (approval, escalation) çağıramaz — read-only tool kısıtı

---

## Event akışı (`ReceiveEventsAsync`)

WebSocket'ten gelen ham frame → JSON parse → `RealtimeServerEvent` enum'una map et:

| OpenAI event tipi | Domain event |
| --- | --- |
| `input_audio_buffer.speech_started` | `SpeechStarted` |
| `input_audio_buffer.speech_stopped` | `SpeechStopped` |
| `conversation.item.input_audio_transcription.completed` | `InputTranscriptCompleted` + transcript |
| `response.created` | `ResponseCreated` |
| `response.output_audio.delta` | `AudioDelta` + base64-decoded PCM bytes |
| `response.output_audio_transcript.delta` | `AssistantTextDelta` + chunk |
| `response.output_audio_transcript.done` | `AssistantTextDone` + full transcript |
| `response.function_call_arguments.done` | `ToolCallReady` (callId, toolName, args JSON) |
| `response.done` | `ResponseDone` |
| `response.cancelled` | `ResponseCancelled` |
| `error` | `Error` + mesaj |
| `session.*` | (filtrelenir — protokol mesajı) |

```csharp
public async IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
```

32 KB buffer kullanır; büyük JSON event'leri çok parçalı frame'de biriktirir ve `EndOfMessage` işaretinde işler. `TryReadFrameAsync` özel metoduna delegelerek `yield` ve `try/catch` arasındaki C# kısıtlamasını aşar. Async enumerable sayesinde caller event'leri **gelir gelmez** işleyebilir (memory dostu). `OperationCanceledException` veya `WebSocketException` → `ConnectionClosed` eventi yayıp akış durur.

---

## Ses I/O detayları

### Browser → OpenAI (mikrofon)

```csharp
public async Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)
    => await SendJsonAsync(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(pcm16) }, ct);
```

`byte[]` (PCM16 24kHz) → base64 → `input_audio_buffer.append` JSON event. Browser WebSocket üzerinden parçalar halinde yollar (~50ms chunk'lar).

### OpenAI → Browser (asistan sesi)

`AudioDelta` event içinde base64 → decode → PCM bytes → Application Browser WebSocket'ine forward.

---

## Native mode tool kataloğu

`RealtimeFunctionTools` sadece **read-only** tool şemaları sunar:

| Tool | Parametre | Amaç |
| --- | --- | --- |
| `product_inquiry_tool` | `product_name` | Ürün fiyat/stok sorgu |
| `product_list_tool` | `category?` | Katalog / kategori listeleme |
| `order_status_tool` | `order_id` | Sipariş durumu |
| `get_last_order_tool` | `customer_id` | Son sipariş |
| `get_all_orders_tool` | `customer_id` | Tüm siparişler |
| `end_conversation` | `reason?` | Sohbeti sonlandır |

### Yasak tool'lar

Native mode'da **kayıt edilmemiştir**:

- `order_placement_tool` — yeni sipariş (HITL gerektirir)
- `order_cancel_tool` — sipariş iptali (HITL gerektirir)
- `return_request_tool` — iade talebi (HITL gerektirir)
- `complaint_registration_tool` — şikayet (HITL gerektirir)
- `human_handoff_tool` — insan çağrısı (text chat'te işler)

Sesli kanal hızlı yanıt için — HITL approval/escalation süreçleri ses akışını bloklar. Bu durumlar **text chat'e yönlendirilir** (system prompt'ta kullanıcıya söyler).

### `end_conversation` özel davranış

Kullanıcı veda ettiğinde model bu tool'u çağırır:

```
Kullanıcı: "Teşekkürler, görüşürüz"
Model: end_conversation(reason: "user_farewell")
Model: (audio) "İyi günler!"
Bridge: WebSocket close
```

Application bu tool çağrısını yakalar, **önce ses biter**, sonra WebSocket kapatılır. Bridge gerçek tool çağrısı yapmaz — sadece sinyal.

### Tool definition formatı

JSON Schema (draft 2020-12):

```csharp
new
{
    type = "function",
    name = "order_status_tool",
    description = "Sipariş durumunu sorgular. Yan etki yoktur.",
    parameters = new
    {
        type = "object",
        properties = new
        {
            order_id = new { type = "string", description = "Sipariş ID, örn: 1" }
        },
        required = new[] { "order_id" }
    }
}
```

OpenAI bu şemaya göre çağrı argümanlarını üretir.

---

## System instructions (Native mode)

OpenAI'a verilen sistem prompt'unun anahtarları:

- **Asistan rolü:** Türk müşteri destek asistanı
- **Yanıt stili:** Kısa, doğal, sesli okunabilir (1-2 cümle)
- **İzinli işlemler:** Yukarıdaki 4 read-only tool
- **Yasak işlemler:**
  - Yeni sipariş oluşturma
  - Şikayet kaydı
  - İade/iptal
  - Ödeme/fatura değişiklikleri
  - Hesap/şifre/kişisel bilgi
- **Yasak işlem talebi olursa:** "Bunu text chat üzerinden yapabilirsiniz" yönlendirmesi
- **Kurallar:**
  - Tool sonucunu yorumla, JSON'u olduğu gibi okuma
  - Eksik ID varsa kullanıcıdan iste
  - Sadece Türkçe
  - Veda olmadan `end_conversation` çağırma

---

## Exception handling

- `OperationCanceledException` veya `WebSocketException` → `TryReadFrameAsync` `(true, null)` döner → `ConnectionClosed` eventi yield edilir, akış durur
- JSON parse hatası → `ParseEvent` `null` döner → o event drop edilir, akış devam eder (OpenAI bazen tanımsız event type döndürebilir)
- `CloseAsync` / `DisposeAsync` → best-effort `NormalClosure` WebSocket kapat; exception yutulur

---

## Performans karakteristikleri

| Metric | Tipik değer |
| --- | --- |
| WebSocket bağlantı | ~200-500 ms |
| Audio chunk RTT | 30-80 ms |
| Tool dispatch RTT | 100-300 ms (DB lookup dahil) |
| First audio response | ~600-1200 ms (kullanıcı konuşma bittikten sonra) |

Native mode bridge mode'dan ~2x hızlı (Application pipeline'ı atlamaz).

---

## Bağlantılar

- [Application RealtimeServices.md](../application/RealtimeServices.md) — RealtimeBridgeService ve RealtimeNativeService kullanımı
- [Options.md](Options.md) — RealtimeOptions alanları
