# Workflow

Bu dokümanda ajanlar arasındaki **akış kontrolünün** nasıl çalıştığı anlatılır: `CustomerSupportChatManager`'ın ajan seçim mantığı, terminasyon koşulları, dinamik handoff mekaniği, güvenlik guard'ları ve **compound query** ile eklenen üst-seviye orkestrasyon.

## İki seviyeli orkestrasyon

Önceki hali workflow **tek seviyeliydi**: MAF `GroupChatManager` tüm konuşmayı yönetirdi. Compound query ile **kod katmanında** ek bir üst seviye eklendi:

```
┌──────────────────────────────────────────────────────────────┐
│ Üst seviye — CustomerSupportTeam (Compound query orkestrasyon)│
│  RunAsync / RunStreamingAsync başında ShouldDecompose?       │
│   ├─ false → tek workflow run (aşağıdaki katmana düş)         │
│   └─ true  → RunDecomposedAsync: N alt-workflow run, sonra    │
│              JoinAggregatedParts ile birleştir               │
└────────────────────────┬─────────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────────┐
│ Alt seviye — MAF GroupChat + CustomerSupportChatManager      │
│  (her workflow run kendi içinde planning→specialist→response)│
└──────────────────────────────────────────────────────────────┘
```

Bu doküman öncelikle **alt seviye**yi (ChatManager'ı) anlatır; sonda "**Compound query orkestrasyonu**" başlığı altında üst seviye açıklanır.

## Microsoft Agent Framework entegrasyonu

Sistem MAF 1.4.0'ın `AgentWorkflowBuilder.CreateGroupChatBuilderWith(…)` API'sini kullanır. AutoGen'deki `SelectorGroupChat`'in MAF karşılığıdır:

```csharp
@Agents/CustomerSupportTeam.cs:120-140
CustomerSupportChatManager? managerRef = null;
_workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents =>
    {
        managerRef = new CustomerSupportChatManager(
            agents, chatClient, _prompts,
            maxMessages: _guards.MaxIterations,
            maxDuplicateToolCalls: _guards.MaxDuplicateToolCalls)
        {
            MaximumIterationCount = _guards.MaxIterations
        };
        return managerRef;
    })
    .AddParticipants(
        planningAgent,
        productInquiryAgent,
        orderPlacementAgent,
        orderInquiryAgent,
        complaintAgent,
        responseAgent)
    .Build();
```

Workflow şablonu **bir kez** derlenir (constructor'da), her istek için yeniden kullanılır. MAF'ın "superstep" tabanlı execution modeli var — her tur (step) bir ajanın tek mesaj üretmesine karşılık gelir.

## Çalıştırma mekanizması

`@Agents/CustomerSupportTeam.cs:198-199`:

```csharp
await using var run = await InProcessExecution.RunStreamingAsync(_workflow, messages);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
```

- `RunStreamingAsync` yeni bir `StreamingRun` açar.
- `TurnToken` gönderilmeden ajanlar **hareket etmez** — MAF'ın mesaj cache + senkron superstep modelinin gereği.
- Event'ler `run.WatchStreamAsync()` üzerinden tüketilir.

### MAF event tipleri

| Event | Açıklaması | `CustomerSupportTeam` içinde kullanımı |
|---|---|---|
| `ExecutorInvokedEvent` | Bir executor başladı | Trace'e `AgentVisit` ekle, `agent running` SSE |
| `ExecutorCompletedEvent` | Bir executor bitti | Visit'i kapat, `agent done` SSE |
| `WorkflowOutputEvent` | Nihai workflow çıktısı (mesaj listesi) | ResponseAgent'ın TERMINATE içeren son mesajını al |
| `WorkflowErrorEvent` | Hata | Trace'e error yaz, 500 veya SSE `error` event |

System-internal executor'lar (`GroupChatHost`, `RoundRobinGroupChatManager`, `StartExecutor`, `EndExecutor`) kullanıcıya agent chip'i olarak gösterilmez — `IsSystemExecutor` filtresi.

---

## `CustomerSupportChatManager`

**Dosya**: `@Agents/CustomerSupportChatManager.cs`
**Base**: `Microsoft.Agents.AI.Workflows.GroupChatManager`

MAF'ın `GroupChatManager` soyut sınıfından türetilmiş iki abstract metodu override eder:

1. `SelectNextAgentAsync(history, ct)` — bir sonraki konuşmacıyı seç
2. `ShouldTerminateAsync(history, ct)` — sohbeti bitir mi?

## Ajan seçim algoritması

`SelectNextAgentAsync` üç-katmanlı bir seçim yapar:

```
┌─────────────────────────────────────────────────────────────┐
│ 1. PlanningAgent JSON gördüm mü?                             │
│    → EVET: plan.NeedsClarification || conf<0.7              │
│           ▸ ResponseAgent'a git                              │
│    → EVET: plan.SelectedAgent dolu                          │
│           ▸ O ajana git                                      │
│    → HAYIR: (parse başarısız) 2'ye geç                      │
└──────────────────┬──────────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────────┐
│ 2. Son mesaj specialist'ten mi?                             │
│    → postToolReflection oku:                                │
│        status=needs_escalation → ResponseAgent              │
│        handoffSuggestion var + ping-pong yok → o ajana     │
│        status=done/failed/partial/needs_followup →         │
│                                       ResponseAgent         │
└──────────────────┬──────────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────────┐
│ 3. Fallback: Varsayılan agent                               │
│    İlk iki katman başarısızsa ResponseAgent'a düşülür       │
└─────────────────────────────────────────────────────────────┘
```

### 1. Katman: PlanningAgent mesajı

```csharp
@Agents/CustomerSupportChatManager.cs:80-106
var lastMessage = history.LastOrDefault();
if (lastMessage?.AuthorName == "PlanningAgent" ||
    (lastMessage?.Text?.Contains("\"selectedAgent\"", …) == true))
{
    var plan = PlanningResultParser.TryParse(lastMessage?.Text);
    if (plan != null)
    {
        LastPlanningResult = plan;
        if (plan.NeedsClarification || plan.IntentConfidence < 0.7)
        {
            // ResponseAgent'a zorla
            return ResponseAgent;
        }
        if (!string.IsNullOrWhiteSpace(plan.SelectedAgent))
        {
            return _agents.FirstOrDefault(a => a.Name == plan.SelectedAgent);
        }
    }
}
```

**Neden agent kimliği + içerik kontrolü?**
MAF bazen `AuthorName`'i ayarlamayabilir (stream sırasında). `"selectedAgent"` anahtarı JSON'da olursa o mesajın PlanningAgent'tan geldiğini içerik üzerinden de doğrulayabiliriz.

### 2. Katman: Specialist'ten gelen `postToolReflection`

```csharp
@Agents/CustomerSupportChatManager.cs:108-153
if (lastMessage != null && IsSpecialistMessage(lastMessage))
{
    var specReasoning = SpecialistReasoningParser.TryParse(
        lastMessage.Text, GetSpecialistName(lastMessage) ?? "specialist");
    var reflection = specReasoning?.PostToolReflection;

    if (reflection != null)
    {
        LastPostToolReflection = reflection;

        // needs_escalation → ResponseAgent + escalation mesajı
        if (reflection.Status == "needs_escalation") return ResponseAgent;

        // Dinamik handoff (ping-pong guard)
        if (handoffSuggestion != null && handoffSuggestion != "ResponseAgent")
        {
            _handoffCounts.TryGetValue(targetName, out var count);
            if (count < MaxHandoffsPerAgent)   // 2
            {
                _handoffCounts[targetName] = count + 1;
                return target;
            }
            // Limit aşıldı → ResponseAgent fallback
        }

        // Varsayılan: ResponseAgent
        return ResponseAgent;
    }
}
```

**Dinamik handoff örneği**: OrderAgent sipariş bulamadı, kullanıcı "şikayet etmek istiyorum" dedi → specialist `handoffSuggestion=ComplaintAgent` üretebilir. ChatManager bunu görüp ComplaintAgent'a geçer. Ama **aynı ajana maksimum 2 kez** handoff yapılabilir — sonra zorla ResponseAgent'a düşer (ping-pong koruması).

### 3. Katman: Varsayılan fallback

İlk iki katman başarısızsa (parse hatası, bilinmeyen mesaj vb.) ResponseAgent'a düşülür.

---

## Terminasyon koşulları

`ShouldTerminateAsync` 3 koşuldan birini true bulursa workflow sonlanır:

```csharp
@Agents/CustomerSupportChatManager.cs:204-237
protected override ValueTask<bool> ShouldTerminateAsync(…)
{
    // Koşul 1: TERMINATE metin kontrolü
    var lastText = history.LastOrDefault()?.Text ?? "";
    if (lastText.Contains("TERMINATE", StringComparison.Ordinal))
    {
        LastTerminationReason = ParseTerminationReason(lastText) ?? "completed";
        return true;
    }

    // Koşul 2: Max YENİ mesaj sayısı
    int newMessageCount = history.Count - _initialHistoryCount;
    if (newMessageCount >= _maxMessages)
    {
        LastTerminationReason = "max_messages_reached";
        return true;
    }

    // Koşul 3: Tekrar eden tool call
    if (DetectRepeatedToolCall(history))
    {
        LastTerminationReason = "repeated_tool_call_guard";
        return true;
    }

    return false;
}
```

### Koşul 1: TERMINATE marker

ResponseAgent her zaman mesajına `TERMINATE: reason=<value>` ekler. Parsing:

| Format | Regex | Reason |
|---|---|---|
| `TERMINATE: reason=completed` | `TERMINATE[:\s]+reason\s*=\s*([a-zA-Z_]+)` | `completed` |
| `TERMINATE (completed)` | `TERMINATE\s*\(([^)]+)\)` | `completed` |
| Sadece `TERMINATE` | - | `completed` (default) |

Reason değerleri:

| Reason | Anlam |
|---|---|
| `completed` | Görev başarıyla tamamlandı |
| `awaiting_user_input` | Kullanıcıdan bilgi gerek (clarification) |
| `escalation_needed` | İnsan desteği talep edildi |
| `not_found` | Aranılan veri yok (ör. sipariş) |
| `error` | Tool hatası |

### Koşul 2: Max iteration

`WorkflowGuardOptions.MaxIterations` (varsayılan 20). **YENİ mesaj** sayısı — history'deki önceki turlar sayılmaz:

```csharp
_initialHistoryCount = history.Count;  // ilk çağrıda kaydedilir
int newMessageCount = history.Count - _initialHistoryCount;
```

Bu kritiktir — 2. tura gelen bir kullanıcıda "geçmişte 10 mesaj vardı, +20 daha mı lazım?" gibi limiti yanlış hesaplamaz.

### Koşul 3: Repeated tool call

Aynı tool'un aynı parametrelerle 3+ kez çağrılması yakalanır:

```csharp
@Agents/CustomerSupportChatManager.cs:263-300
private bool DetectRepeatedToolCall(IReadOnlyList<ChatMessage> history)
{
    var recent = history.TakeLast(10);
    foreach (var msg in recent)
    {
        foreach (var content in msg.Contents ?? Array.Empty<AIContent>())
        {
            if (content is FunctionCallContent fc)
            {
                var signature = BuildToolSignature(fc.Name, fc.Arguments);
                // deterministik: sorted param key=value join
                _toolCallCounts[signature]++;
                if (_toolCallCounts[signature] >= _maxDuplicateToolCalls) return true;
            }
        }
    }
    return false;
}
```

`BuildToolSignature("order_status_tool", {orderId: "ORD-1"})` → `"order_status_tool:orderId=ORD-1"` (sıralı).

---

## Guard'lar (WorkflowGuardOptions)

`@Models/WorkflowGuardOptions.cs`:

```json
"WorkflowGuards": {
  "TimeoutSeconds": 60,
  "MaxDuplicateToolCalls": 3,
  "MaxTokensPerRequest": 30000,
  "MaxIterations": 20
}
```

| Guard | Nerede uygulanır | Amaç |
|---|---|---|
| `TimeoutSeconds` | `CustomerSupportTeam.RunStreamingAsync` — linked CTS | Genel saatim |
| `MaxIterations` | `CustomerSupportChatManager` → `MaximumIterationCount` + `ShouldTerminateAsync` | Sonsuz loop önleme |
| `MaxDuplicateToolCalls` | `DetectRepeatedToolCall` | Aynı tool'un takılmasını yakala |
| `MaxTokensPerRequest` | Modelde var, `trace.EstimatedTokens` üzerinden rapor (henüz aktif enforcement yok) | Maliyet tavanı (ileride) |

### Timeout akışı

```csharp
@Agents/CustomerSupportTeam.cs:331-335
using var timeoutCts = new CancellationTokenSource(
    TimeSpan.FromSeconds(_guards.TimeoutSeconds));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
var effectiveCt = linkedCts.Token;
```

Timeout vs. client cancellation ayrı ayrı takip edilir — timeout yüzünden iptal olduysa kullanıcıya anlaşılır mesaj gösterilir:

```csharp
@Agents/CustomerSupportTeam.cs:450-459
if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
{
    _traceStore.Complete(trace.TraceId, terminationReason: "timeout", …);
    yield return new StreamEvent(StreamEventTypes.Error,
        new { message = $"İşlem {_guards.TimeoutSeconds} saniyede tamamlanamadı." });
    yield break;
}
```

---

## Bir turun mesaj akışı

Tipik bir başarılı turda mesaj sırası:

```
[0] (System) Müşteri bağlamı: CUST-001 ...
[1] (System) [ÖN-ANALİZ REASONING ÇIKTISI] intent=sipariş_sorgulama ...
[2] (System) [ENTITY EXTRACTION] order_id=ORD-1, ÖNCELİK: order_status_tool kullan
[3] (User)   "ORD-1 siparişim nerede?"
───────────────── Workflow başlar ─────────────────
[4] (Asst: PlanningAgent)     ```json{"selectedAgent":"OrderAgent"...}``` 1. OrderAgent: …
    ▸ ChatManager: plan.SelectedAgent=OrderAgent → ORA geç
[5] (Asst: OrderAgent) ```json{"preToolCheck":{"canProceed":true}}``` + [ToolCall: order_status_tool(orderId=ORD-1)]
[6] (Tool)                    {"Success":true,"Data":{"orderId":"ORD-1","status":"Kargolandı"...}}
[7] (Asst: OrderAgent) ```json{"postToolReflection":{"status":"done","handoffSuggestion":"ResponseAgent"}}``` + kısa özet
    ▸ ChatManager: reflection.status=done → ResponseAgent'a geç
[8] (Asst: ResponseAgent)     "ORD-1 siparişiniz kargolandı…
                               TERMINATE: reason=completed"
    ▸ ChatManager: TERMINATE tespit edildi → ShouldTerminate=true
───────────────── Workflow biter ─────────────────
```

Sonrasında:
- `RemoveTerminationMarkers` + `RemoveTechnicalJsonBlocks` ile temizleme
- Kullanıcıya: "ORD-1 siparişiniz kargolandı…"
- Trace kayıt: `TerminationReason=completed`, `IterationCount=4`

### Admin Replan override (one-shot)

Admin "🔄 Yeniden Planla" tetiklerse `BuildWorkflowMessagesAsync` `state.ForceReplanNextTurn` flag'ini görür ve yukarıdaki sıraya **`User` mesajından önce** ek bir system mesajı ekler:

```
[3a] (System) 🔄 ADMIN OVERRIDE — Önceki TOOL ÇAĞRILARINI ve specialist agent kararlarını
              geçersiz say. Ancak müşteri ile temsilci arasında geçen son yazışmalardaki
              bağlamı AYNEN KORU ve dikkate al. ...
              📌 Admin notu (sadece sana, müşteri görmez): "şikayet kaydı aç"
```

Flag + `state.ReplanNote` tek seferlik kullanıldıktan sonra temizlenir. PlanningAgent bu hint'i gördükten sonra önceki tool sonuçlarını yok sayar ama admin/müşteri konuşma bağlamına göre yeniden planlar. Ayrıntı → [patterns.md#204-admin-replan](patterns.md#204-admin-replan-one-shot-planning-override--auto-bot-turn).

---

## Output extraction (çıktı alma)

MAF `WorkflowOutputEvent` ile nihai mesaj listesini verir. Biz içinden doğru mesajı seçmek için öncelik sıralaması kullanırız:

```csharp
@Agents/CustomerSupportTeam.cs:713-736
private string ExtractResultFromOutput(WorkflowOutputEvent output)
{
    if (output.Data is IEnumerable<ChatMessage> chatMessages)
    {
        // Öncelik 1: TERMINATE içeren mesaj (ResponseAgent)
        var terminateMsg = chatMessages.LastOrDefault(m => m.Text?.Contains("TERMINATE") ?? false);
        if (terminateMsg != null) return terminateMsg.Text!;

        // Öncelik 2: Son assistant mesajı (routing değilse)
        var lastAssistantMsg = chatMessages.LastOrDefault(m =>
            m.Role == ChatRole.Assistant && !IsInternalRoutingMessage(m.Text));
        return lastAssistantMsg?.Text ?? "";
    }
    …
}
```

`IsInternalRoutingMessage` — ajan adını içeren dahili mesajları yakalar (ör. "1. OrderAgent: …"). Bu mesajlar kullanıcıya gösterilmemeli — fallback olarak `RewriteRoutingMessageAsync` ile LLM'e yeniden yazdırılır.

## Çıktı temizleme pipeline'ı

Kullanıcıya yanıt vermeden önce:

```
raw result
  ▼
CleanTerminateMarker        # "TERMINATE: reason=..." ve sonrasını sil
  ▼
StripTechnicalJsonBlocks    # preToolCheck/resultConfidence/postToolReflection JSON'larını sil
  ▼
IsInternalRoutingMessage?   # Agent adı içeriyorsa...
  ▼ yes
RewriteRoutingMessageAsync  # LLM ile kullanıcı dostu metne çevir
  ▼
final text → SSE stream / JSON response
```

---

## Observability (trace)

Her workflow koşusu için `IReasoningTraceStore.StartTrace` çağrılır ve her adımda `Update` ile güncellenir:

| Adım | Trace alanı |
|---|---|
| İstek geldi | `TraceId`, `SessionId`, `UserQuery`, `StartedAt` |
| Reasoning tamamlandı | `Reasoning` |
| Her agent başladı/bitti | `AgentVisits[].StartedAt/CompletedAt/Duration` |
| Workflow bitti | `Planning`, `SpecialistReasonings[]` |
| Tamamlama | `CompletedAt`, `TerminationReason`, `FinalResponse`, `IterationCount` |

`GET /traces/recent?count=20` ile listelenir, `GET /traces/{traceId}` ile tam detay alınır. Ring buffer — son 500 trace tutulur.

---

## Özet

- **Workflow** = 7 ajan + `CustomerSupportChatManager` (MAF `GroupChatManager` türevi)
- **Ajan seçim** 3 katmanlı: PlanningAgent JSON → specialist reflection → varsayılan (ResponseAgent)
- **Terminasyon** 3 koşul: TERMINATE marker | max iteration | repeated tool call
- **Guard'lar** appsettings'den konfigüre edilir; timeout, max iteration, duplicate tool protection
- **Çıktı** temizleme pipeline'ı TERMINATE + teknik JSON + routing sızıntısını arındırır
- **Observability**: her koşu bir `ReasoningTrace` üretir ve endpoint'lerle sorgulanabilir
- **Üst seviye (Compound query)**: compound query algılanırsa tek workflow yerine N workflow sıralı koşturulur ve sonuçlar birleştirilir

---

## Compound query orkestrasyonu

**Dosya**: `@Agents/CustomerSupportTeam.cs:912-1192` (helper bloğu)

### Neden?

MAF `GroupChatManager.SelectNextAgentAsync` bir turda **tek next speaker** seçer. Doğal compound query'de (*"ORD-1 nerede ve ORD-2 için şikayet aç"*) tek workflow tek specialist çalıştırıp ResponseAgent'a düşer — ikinci görev unutulur veya kullanıcıdan tekrar sorulur (ping-pong).

### Çözüm

`CustomerSupportTeam.RunAsync` ve `RunStreamingAsync` **başlarında** reasoning sonucunu kontrol eder:

```csharp
if (ShouldDecompose(reasoning))
    return await RunDecomposedAsync(query, history, session, reasoning!);
    // veya streaming varyantı
```

### ShouldDecompose kuralı

```csharp
private static bool ShouldDecompose(ReasoningResult? r)
{
    if (r == null || r.SubTasks.Count < 2) return false;
    var distinctAgents = r.SubTasks
        .Select(s => s.TargetAgent)
        .Where(a => !string.IsNullOrWhiteSpace(a))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    return distinctAgents >= 2;   // Aynı agent 2 subtask tek workflow ile yeterli
}
```

### Akış

```
RunAsync(query, reasoning)
    │
    └─ ShouldDecompose=true
        └─ RunDecomposedAsync:
              │
              ├─ runningHistory = [conversation + user query]
              │
              ├─ for subTask in reasoning.SubTasks.OrderBy(Order):
              │     │
              │     ├─ subQuery = BuildSubQuery(subTask)
              │     │    → "ORD-1 için durum sorgula (order_id=ORD-1)"
              │     │
              │     ├─ subReasoning = DeriveSubReasoning(parent, subTask)
              │     │    → yeni ReasoningResult:
              │     │      • Intent = subTask.Intent
              │     │      • NextAction = "{TargetAgent} ile alt görevi yürüt"
              │     │      • SubTasks = []          ← Recursive loop koruması
              │     │      • SanityIssues = []
              │     │      • Steps = []
              │     │
              │     ├─ subResponse = await RunAsync(
              │     │      subQuery, runningHistory, session, subReasoning)
              │     │    (Recursive çağrı — SubTasks boş olduğundan tek-workflow
              │     │     dalına düşer, tam planning→specialist→response yapılır)
              │     │
              │     ├─ parts.Add(FormatSubResult(subTask, subResponse))
              │     │    → "**{order}) {description}**\n\n{response}"
              │     │
              │     └─ runningHistory.Add(sub user + assistant msgs)
              │        (Sonraki subtask önceki sonucu context olarak görür)
              │
              └─ return JoinAggregatedParts(parts)
                 → "**1) ...**\n\n<r1>\n\n---\n\n**2) ...**\n\n<r2>"
```

### Helper'lar (`CustomerSupportTeam.cs:912-1192`)

| Helper | Sorumluluk |
|---|---|
| `ShouldDecompose` | Decompose kararı (2+ subtask, 2+ farklı agent) |
| `DeriveSubReasoning` | Mini-reasoning üret — `SubTasks=[]` ile recursion-safe |
| `BuildSubQuery` | `"{description} ({entity1=val1, entity2=val2})"` |
| `FormatSubResult` | `**{order}) {description}**\n\n{response}` |
| `JoinAggregatedParts` | `\n\n---\n\n` ayırıcıyla birleştir |
| `RunDecomposedAsync` | Non-streaming orkestrasyon (recursive `RunAsync`) |
| `RunDecomposedStreamingAsync` | Streaming orkestrasyon (event forwarding + aggregated response) |
| `ExtractTextFromDelta` | Anonymous `{text=...}` objesinden metni okur (reflection) |

### Streaming davranışı

`RunDecomposedStreamingAsync` iç `RunStreamingAsync` event'lerini şu şekilde işler:

| İç event | Dış event | Not |
|---|---|---|
| `agent` | `agent` (forward) | PlanningAgent / specialist / ResponseAgent transitions UI'a yansır |
| `reasoning_*` | — (yutulur) | Subtask için yeni reasoning çağrısı yapılmaz |
| `response_delta` | — (biriktirilir) | `subResponseBuilder`'a eklenir |
| `response_start` / `response_complete` | — (yutulur) | Final aggregated çıktıda tek başına yayınlanır |
| `error` | `error` (forward) | Subtask hatası üst seviyeye iletilir |

Ek olarak orkestrasyon kendisi şu event'leri yayar:

```
agent: { name: "Orchestrator", status: "decomposing", subTaskCount: N }
agent: { name: "SubTask#1", status: "running", description, targetAgent, order, total }
[iç workflow event'leri forward edilir]
agent: { name: "SubTask#1", status: "done", order }
... (N kez)
agent: { name: "Orchestrator", status: "aggregating" }
response_start:    { decomposed: true, subTaskCount: N, terminationReason: "completed" }
response_delta:    { text: chunk }           (ChunkTextAsync ile kelime kelime)
response_complete: { text, decomposed: true, subTaskCount: N, ... }
```

### Maliyet

| Senaryo | LLM çağrısı |
|---|---|
| Normal tek-görev | 4 (reasoning + planning + specialist + response) |
| 2 subtask compound | ~7 (1 reasoning + 2×planning + 2×specialist + 2×response) |
| N subtask | ~1 + 3N |

Üst seviye reasoning (compound algılayan) **bir kez** çalışır. Her alt subtask kendi planning + specialist + response döngüsünü yapar — bu, mini-reasoning üretme maliyeti yerine parent reasoning'in bir kere daha amortize edilmesini sağlar.

### Trace davranışı

Her subtask recursive `RunAsync` çağrısı **kendi** `ReasoningTrace`'ini üretir. Yani compound query N trace'e yansır (hepsi aynı session'da). `/traces/by-session/{sessionId}` endpoint'i ile sıralı görülebilir. Üst seviye orkestrasyonun ayrı trace'i yoktur — bunun yerine session geçmişinde N+1 mesaj oluşur (user query + N birleşmiş response).

### Sınırlamalar

- **Paralel değil, sıralı**: Subtask'ler sırayla çalışır (`foreach`). İlk subtask tamamlanmadan ikincisi başlamaz.
- **N trace**: Her subtask bağımsız trace üretir. Üst seviye için tek bir "parent trace" yoktur — ileride `ReasoningTrace.ParentTraceId` gibi bir alan eklenebilir.
- **Dependency graph yok**: `SubTask.Dependencies` alanı şu an **şeffaf** — helper'lar sadece `Order` alanına göre sıralama yapar. İleride topolojik sıralama / paralel execution eklenebilir.
