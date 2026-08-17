# Chat Akışını Debug Etme

Bu doküman bir kullanıcı mesajının baştan sona nasıl işlendiğini **kod seviyesinde** anlatır: hangi dosyada hangi metoda breakpoint koyacaksınız, neyi izleyeceksiniz, yaygın senaryolar.

> Yüksek seviye akış için: [`reasoning.md`](reasoning.md).  
> Endpoint detayları için: [`CustomerSupportBot.Api/Endpoints-Chat.md`](CustomerSupportBot.Api/Endpoints-Chat.md).

---

## Tam request lifecycle

```
[Browser]
  POST /chat/stream  { sessionId, message }
       │
       ▼
[ASP.NET pipeline]
  AuthorizedHttpClientHandler (Web tarafı) → Bearer header
  Api: UseAuthentication → UseAuthorization (anonim — chat public)
       │
       ▼
[Endpoint handler — ChatEndpoints.cs]
  • InputGuard.Inspect(message)
  • IChatPort.HandleStreamAsync(...)
       │
       ▼
[ChatPortService.HandleStreamAsync]
  • Session lookup/create
  • SessionStateExtractor (deterministic) — turn++, intent keyword
       │
       ▼
[ReasoningService.AnalyzeAsync]
  • IdExtractor.Extract → ExtractedIds
  • Prompt build (system + history + hint)
  • IReasoningChatClient.StreamAsync → JSON parse
  • ReasoningResultParser → ReasoningResult
  • ReasoningSanityChecker → SanityIssues
       │
       ▼
[PlanningAgent (MAF)]
  • IGeneralChatClient.CompleteAsync
  • PlanningResultParser → PlanningResult
  • needsClarification? → ResponseAgent direkt
       │
       ▼
[Specialist agent — CustomerSupportTeam]
  • PreToolCheck (canProceed?)
  • Yan-etkili tool? → ApprovalGateService.RequestAsync
       │  (admin onay verdi)
       ▼
  • Tool.InvokeAsync → ToolResult
  • PostToolReflection (status, handoffSuggestion?)
       │
       ▼
[ResponseAgent]
  • Synthesis — son yanıtı kullanıcıya formatla
       │
       ▼
[Trace persist + SSE response]
  • IReasoningTraceStore.Complete
  • SseForwarder.WriteAsync(chunk events)
       │
       ▼
[Browser]
   chat-api-client.js → SSE parse → chat-ui.js render
```

---

## Önemli breakpoint noktaları

### 1. Request giriş — `ChatEndpoints.cs`

**Ne için?** İstek geldi mi? InputGuard reddetti mi? SessionId doğru çözüldü mü?

```csharp
// Endpoints/ChatEndpoints.cs — chatGroup.MapPost("/stream", ...)
async (request, ct) =>
{
    var guardResult = await guard.InspectAsync(request.Message);    // ← BP
    if (!guardResult.IsAllowed) return Results.BadRequest(...);

    await chatPort.HandleStreamAsync(request.SessionId, request.Message, sse, ct);
};
```

**İzlenecek değişkenler:**
- `request.SessionId` — null/empty ise downstream'de yeni session oluşturulur
- `guardResult.RejectReason` — "input_too_long", "injection_pattern", vb.

### 2. Session state preprocessing — `ChatPortService.cs`

```csharp
public async Task HandleStreamAsync(string sessionId, string message, ...)
{
    var session = await _sessionManager.GetOrCreateAsync(sessionId);    // ← BP

    SessionStateExtractor.ExtractAndApply(
        session.State, message, botResponse: "", turnNumber: ++turn);    // ← BP
    ...
}
```

**İzlenecek değişkenler:**
- `session.State.CollectedInfo` — `customer_id`, `order_id` çıkarıldı mı?
- `session.State.CurrentIntent` — keyword tablosu intent tespit etti mi?
- `session.State.ConsecutiveNegativeTurns` — auto-escalation eşiğinde mi?

### 3. Reasoning analizi — `ReasoningService.cs`

**Ne için?** "Bot niye yanlış düşünüyor?" sorusunun ana noktası.

```csharp
public async Task<ReasoningResult> AnalyzeAsync(...)
{
    var ids = IdExtractor.Extract(userQuery);    // ← BP — regex extraction
    var hint = IdExtractor.BuildHintMessage(ids);

    var prompt = _messageBuilder.Build(userQuery, history, hint);    // ← BP

    var rawJson = await _reasoningClient.CompleteAsync(prompt, ct);    // ← BP — LLM call

    var result = ReasoningResultParser.Parse(rawJson);    // ← BP — parse fail riski

    var issues = _sanityChecker.Check(result);    // ← BP
    result.SanityIssues = issues;

    return result;
}
```

**İzlenecek:**
- `rawJson` — LLM tam ne döndü? JSON parse edilebilir mi?
- `result.Steps[].Grounding` — çoğu `assumption` mu? (kötü işaret)
- `result.ConfidenceScore` — `< 0.5` ise downstream'de clarification
- `issues` — Error severity varsa replan tetiklenir

### 4. PlanningAgent — `CustomerSupportTeam.cs`

```csharp
// PlanningAgent'ın system prompt'una sahip MAF ChatAgent
var planningResponse = await planningAgent.RunAsync(prompt, ct);    // ← BP
var planning = PlanningResultParser.Parse(planningResponse.Text);    // ← BP

if (planning.NeedsClarification)
{
    // Specialist çağrılmaz, doğrudan ResponseAgent
    return ResponseFor(planning.ClarificationQuestion);    // ← BP
}
```

**İzlenecek:**
- `planning.SelectedAgent` — beklenen agent mı?
- `planning.AlternativesRejected` — niye diğerleri elendi?
- `planning.NeedsClarification` — `true` ise ResponseAgent'a düşer (artık sayısal eşik yok, bkz. [reasoning.md §4](reasoning.md))
- `reasoning.ConfidenceScore` — `ReasoningSanityChecker`'ın iç tutarlılık kontrollerinde kullanılır

### 5. Specialist + PreToolCheck — `CustomerSupportChatManager.cs`

```csharp
// MAF group chat — her specialist'in turn'ünde:
var specialistResponse = await specialist.RunAsync(...);    // ← BP

var reasoning = SpecialistReasoningParser.Parse(specialistResponse.Text);

if (reasoning?.PreToolCheck?.CanProceed == false)    // ← BP
{
    // Eksik bilgi var, kullanıcıya soru sor
    return AskUserAgain(reasoning.PreToolCheck.MissingParams);
}
```

**İzlenecek:**
- `reasoning.PreToolCheck.MissingParams` — eksik ne?
- `reasoning.PreToolCheck.CollectedParams` — session'dan ne alındı?

### 6. Approval Gate (HITL) — `ApprovalGateService.cs`

**Ne için?** "Tool niye çağrılmadı / niye bekledi?" sorusu.

```csharp
public async Task<bool> RequestApprovalAsync(string toolName, ...)
{
    if (!_options.ToolsRequiringApproval.Contains(toolName))
        return true;    // ← BP — ToolsRequiringApproval listesinde değilse direkt geçer

    var approval = new ApprovalRequest(...);
    await _approvalQueue.EnqueueAsync(approval);    // ← BP — DB + Redis pub/sub

    var decided = await _approvalQueue.AwaitDecisionAsync(approval.Id, timeout);    // ← BP — bloklayan await!
    return decided?.Status == ApprovalStatus.Approved;
}
```

**İzlenecek:**
- `toolName` — `ApprovalOptions.ToolsRequiringApproval` listesinde mi? (default: `order_placement_tool`, `complaint_registration_tool`)
- `decided` — null = timeout (SLA Guardian devre dışıysa veya OnTimeout=None ise sonsuza kadar bekler)

**Yaygın sorun:** Admin paneli açık değil → onay gelmiyor → request hang. Çözüm:
- `Approval:Enabled = false` ile bypass et (dev)
- `Approval:TimeoutSeconds` ile kısa tut + AutoReject

### 7. Tool çağrısı — Tool sınıfları

Tool’lar `Application/Services/CustomerSupportToolsService.cs` içinde tanımlıdır. Her tool method'unun başı:

```csharp
[Tool("order_status_tool", "...")]
public async Task<ToolResult> InvokeAsync(string orderId, ...)
{
    if (string.IsNullOrEmpty(orderId))    // ← BP
        return ToolResult.ValidationError("MISSING_REQUIRED_FIELD", ...);

    var order = await _orderRepo.GetAsync(orderId);    // ← BP — DB call
    if (order is null) return ToolResult.NotFound("Order", orderId);

    return ToolResult.Ok(order, $"Sipariş {orderId} durumu: {order.Status}");
}
```

**İzlenecek:**
- Parametre değerleri — null/empty?
- `ToolResult.Success` — false ise neden?
- `ToolResult.Error.Code` — `MISSING_REQUIRED_FIELD` / `NOT_FOUND` / `STOCK_INSUFFICIENT`

### 8. PostToolReflection + Handoff

```csharp
var reflection = reasoning.PostToolReflection;
if (reflection?.HandoffSuggestion != null)    // ← BP
{
    // AgentTeamCoordinator yeni agent'a yönlendirir
    nextAgent = ResolveAgent(reflection.HandoffSuggestion);
}
```

**İzlenecek:**
- `reflection.Status` — `needs_followup` / `needs_escalation` durumları
- `reflection.HandoffSuggestion` — yeniden routing var mı?

### 9. SSE stream output — `SseForwarder.cs`

```csharp
await _sse.WriteAsync("reasoning_complete", reasoningResult, ct);    // ← BP
await _sse.WriteAsync("response_delta", chunk, ct);
await _sse.WriteAsync("done", new { sessionId }, ct);
```

**İzlenecek:**
- `Response.HasStarted` — true ise exception artık yansıtılamaz, sadece log
- Client disconnect → `OperationCanceledException` (sessizce yutulur)

---

## Senaryo bazlı debug rehberleri

### S1. "Yanlış agent seçildi"

**Belirti:** "Şikayetim var" diyen kullanıcıya OrderAgent yanıt veriyor.

**İzlenecek yol:**
1. `ReasoningService.AnalyzeAsync` → `result.Intent` ne? (`Complaint` bekleniyor)
   - Yanlışsa → prompt veya keyword tablosu problemi
2. `PlanningAgent` → `planning.SelectedAgent` ne?
   - `OrderAgent` ise → planning prompt'unda alternatif logic incelenmeli
3. `planning.AlternativesRejected` — `ComplaintAgent` niye elenmiş?
4. `PostToolReflection.HandoffSuggestion` — runtime correction var mı?

**Hızlı tanı:** `/traces/{traceId}` endpoint'i → tüm reasoning + planning JSON görülür.

### S2. "Bot kullanıcıyı tekrar tekrar soruyor"

**Belirti:** Kullanıcı 1 verdi ama bot hala "sipariş numaranız nedir?" diyor.

**İzlenecek yol:**
1. `SessionStateExtractor.ExtractAndApply` → `session.State.CollectedInfo` doluyor mu?
2. `IdExtractor.Extract` → 1 yakalıyor mu? (regex match)
3. `ReasoningResult.RequiredInfo` — gereksiz tekrar mı? (SanityIssues'da `redundant_required_info` arar)
4. PreToolCheck → `CollectedParams`'a ekleniyor mu?

**Yaygın neden:** `Prompt/services/reasoning-system.md`'de session.state nasıl okunacak açıklamasının eksikliği.

### S3. "HITL approval bekliyor, sıkıştı"

**Belirti:** UI'da "İşleminiz işleniyor..." takılı.

**İzlenecek yol:**
1. `ApprovalGateService.RequestApprovalAsync` → DB'de approval kaydı var mı?
2. Admin paneli `/admin` → "Approvals" tab'ında görünüyor mu?
3. Redis pub/sub çalışıyor mu? → `csbot:approval:created` kanalı
4. `appsettings.json` → `Approval:Enabled` (false ise bypass)
5. `Sla:Approval:OnBreach` — `AutoReject` ise SLA Guardian halletmeli

**Hızlı çözüm:** Dev'de `Approval:Enabled = false` veya `TimeoutSeconds: 30 + OnTimeout: AutoApprove`.

### S4. "LLM JSON parse hatası"

**Belirti:** Bot fallback mesajı veriyor (`"Şu anda analiz yapamıyorum..."`).

**İzlenecek yol:**
1. Log'da `[ReasoningResultParser] JSON parse failed` var mı?
2. `rawJson` değişkenine bak — LLM `` ```json `` fence'siz veya bozuk format mı döndü?
3. `ReasoningResultParser.ExtractJsonBlock` — fence'siz JSON'u yakalamayı dener
4. Model `max_tokens`'e takılmış olabilir → JSON kesiliyor; daha yüksek limit dene

**Önleme:** Prompt'un sonunda örnek JSON ver. `temperature: 0.1` ile deterministic.

### S5. "Streaming yavaş / takılıyor"

**Belirti:** SSE chunk'ları geç geliyor veya gelmiyor.

**İzlenecek yol:**
1. `agentVisits[].durationMs` — hangi agent uzun sürüyor?
2. `ReasoningChatClient.StreamAsync` — `IAsyncEnumerable` yield ediyor mu?
3. `SseForwarder.WriteAsync` — `await Response.Body.FlushAsync()` çağrıldı mı?
4. Reverse proxy (nginx) buffer'ı kapalı mı? → `X-Accel-Buffering: no` header

**Tanı:** `/telemetry/cost` → `averageLatencyMs` per-model. Yüksekse model değiştir.

---

## TraceId ile tek request'i takip

Her request bir `traceId` taşır (`Activity.Current.Id`). Log'da bu ID ile filter yap:

```bash
# Sadece bu request'in log'larını gör
grep "trace_01HG8K2P3M9X4N7B5R" logs/app.log
```

API → Application → Adapters tüm katmanlar aynı traceId'yi taşır (OpenTelemetry context propagation).

**Cross-reference:**
- Log → traceId bulundu
- `/traces/{traceId}` endpoint → tam reasoning/planning/agentVisits/toolCalls JSON
- `/replay?traceId=...` → step-by-step görsel walkthrough (admin paneli)
- Jaeger UI → distributed span graph (OTLP exporter aktifse)

---

## Yaygın tuzaklar

### 1. Fire-and-forget task'lar

`PersistAsync`, `LessonMiner.MineAsync`, `CostUsageStore.PersistAsync` arka planda çalışır:

```csharp
_ = Task.Run(async () => await _persistence.RecordAsync(record));
```

Bu task fail olursa main thread görmez. Log'a bakmak gerekir.

**Çözüm:** `TaskScheduler.UnobservedTaskException` handler — `Program.cs`'de zaten kayıtlı, log'a düşer.

### 2. JS interop async issue

Blazor `[JSInvokable]` metodları `await InvokeAsync(StateHasChanged)` çağırmazsa UI güncellenmez:

```csharp
[JSInvokable]
public Task OnStreamEvent(string type, string data)
{
    // ...
    StateHasChanged();   // ← YETERSIZ — JS thread'inden çağrıldığı için
    return InvokeAsync(StateHasChanged);   // ← DOĞRU
}
```

### 3. SSE response.HasStarted

Bir kez SSE event yazıldıktan sonra `DomainExceptionHandler` HTTP'ye yansıtamaz:

```csharp
if (httpContext.Response.HasStarted) return false;
// Exception artık client'a JSON gönderemez — sadece log + connection close
```

Stream ortasında hata → kullanıcı yarım yanıt görür. Defensive coding: validation'ı LLM çağrısından önce yap.

### 4. DotNetObjectReference leak

```csharp
private DotNetObjectReference<Chat>? _dotNetRef;
// Dispose unutulursa component GC'lenmez → memory leak
public void Dispose() => _dotNetRef?.Dispose();
```

Page navigation'da bunu unutmak browser'da memory artırır.

### 5. Approval timeout vs SLA Guardian

`Approval:TimeoutSeconds` ve `Sla:Approval:BreachThresholdSeconds` farklı:
- TimeoutSeconds: `AwaitDecisionAsync` ne kadar bekler (client perspective)
- BreachThresholdSeconds: SLA event ne zaman warn/breach üretir (system perspective)

İkincisini birinciden büyük yaparsanız SLA hiç tetiklenmez.

---

## Hızlı dev workflow

```
1. Reasoning prompt'unu değiştir:
   Prompts/services/reasoning-system.md → düzenle → restart

2. Hangi agent çağrıldı gör:
   GET /traces/recent?count=5 → agentVisits dizisi

3. Tool sonucunu tara:
   GET /traces/{traceId} → toolCalls[].resultSummary

4. Replay ile step-by-step izle:
   GET /replay?traceId=... → admin paneli görsel walkthrough

5. Cost kontrolü:
   GET /telemetry/cost → per-model latency + tokens + USD
```

---

## İlgili dokümanlar

- [`reasoning.md`](reasoning.md) — Reasoning pattern'leri + örnek trace
- [`CustomerSupportBot.Api/Endpoints-Chat.md`](CustomerSupportBot.Api/Endpoints-Chat.md) — Endpoint detayları + SSE event tipleri
- [`CustomerSupportBot.Application/Reasoning/ReasoningService.md`](CustomerSupportBot.Application/Reasoning/ReasoningService.md), [`CustomerSupportBot.Application/Chat/InputGuard.md`](CustomerSupportBot.Application/Chat/InputGuard.md)
- [`CustomerSupportBot.Adapters.Agents/README.md`](CustomerSupportBot.Adapters.Agents/README.md) — MAF agent ekibi
- [`developer-guide.md`](developer-guide.md) — "X yapmak istiyorum" rehberi
- [`operations.md`](operations.md) — Konfigürasyon + sorun giderme
