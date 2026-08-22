# Sohbet ve Gerçek Zamanlı Uç Noktaları (Chat & Realtime)

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Endpoints/ChatEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/RealtimeEndpoints.cs`
  - `CustomerSupportBot.Api/Endpoints/SessionEndpoints.cs`
- **Namespace:** `CustomerSupportBot.Api.Endpoints`

## 1. `ChatEndpoints` (`/api/chat`)

- **`POST /api/chat` (Senkron Yanıt):**
  - Girdi: `ChatRequest { SessionId, Query }`
  - Mantık: `IChatOrchestratorPort.ProcessAsync` çağrılır; akıl yürütme ve çoklu ajan iş akışı tamamlanana kadar bekler ve JSON `ChatResponse` döner.
- **`POST /api/chat/stream` (SSE Canlı Akış):**
  - Girdi: `ChatRequest`
  - Çıktı: `text/event-stream` formatında `StreamEvent` SSE akışı (`agent_started`, `tool_called`, `response_delta`, `response_completed`).
  - Mantık: `IChatOrchestratorPort.ProcessStreamingAsync` çıktısını [SseWriter](../Infrastructure/SseAndWebSockets.md) ile istemciye gerçek zamanlı yazar.

---

## 2. `RealtimeEndpoints` (`/api/realtime`)

- **`GET /api/realtime/ws` (WebSocket Ses Akışı):**
  - Protokol: Çift yönlü WebSocket.
  - Mantık: İstemci bağlantısını `AcceptWebSocketAsync` ile kabul eder; [WebSocketBrowserChannel](../Infrastructure/SseAndWebSockets.md) üzerinden tarayıcı mikrofonundan gelen PCM16 ses baytlarını [IRealtimeVoiceTransport](../../CustomerSupportBot.Application/Ports/Outbound/AI/IRealtimeVoiceTransport.md) üzerinden OpenAI Realtime API'ye iletir ve modelin ses yanıtını anında tarayıcıya geri akıtır.

---

## 3. `SessionEndpoints` (`/api/sessions`)

- **`GET /api/sessions/{sessionId}`**: Oturum durumunu ve mesaj geçmişini döner.
- **`DELETE /api/sessions/{sessionId}`**: Oturumu temizler/sıfırlar.
- **`POST /api/sessions/{sessionId}/replan`**: Yönetici müdahalesi ile bir sonraki tur için zorunlu yeniden planlama notu enjekte eder.
