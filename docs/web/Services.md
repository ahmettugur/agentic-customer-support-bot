# API Service'leri

**Klasör:** `Services/`

API endpoint'lerini wrap eden client sınıfları. Sayfalar (Razor) bunları `@inject` ile alır, doğrudan `HttpClient` kullanmaz.

| Service | Endpoint prefix | Sayfa |
|---|---|---|
| `AdminApiService` | `/approvals`, `/escalations`, `/chat-sessions`, `/improvements`, `/agents` | Admin |
| `ChatApiService` | `/chat`, `/sessions` | Chat |
| `AnalyticsApiService` | `/analytics` | Admin (Analytics tab) |
| `TracesApiService` | `/traces`, `/chat-sessions/.../history` | Traces, Replay |
| `SlaApiService` | `/sla` | Sla |
| `WorkflowApiService` | `/workflows` | WorkflowDesigner |

Hepsi `AuthorizedHttpClientHandler` ile JWT token otomatik inject eder.

---

## Genel pattern

```csharp
public sealed class SomeApiService
{
    private readonly HttpClient _http;
    private readonly ILogger<SomeApiService> _logger;

    public SomeApiService(HttpClient http, ILogger<SomeApiService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<TResponse?> SomeMethodAsync(...)
    {
        try
        {
            var resp = await _http.GetFromJsonAsync<TResponse>("/some/endpoint");
            return resp;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "API call failed");
            throw;
        }
    }
}
```

**Konvansiyonlar:**
- `HttpClient` constructor injection (DI'dan)
- JSON serialization → `System.Net.Http.Json` extensions
- Exception loglanır, caller'a yükselir
- Cancellation token ekstra parametre

---

## AdminApiService

Admin paneli'nin tüm endpoint çağrıları. **Role-aware prefix** alır:

```csharp
public sealed class AdminApiService
{
    // Approvals
    public async Task<List<ApprovalRequest>> GetPendingApprovalsAsync(string prefix)
        => await _http.GetFromJsonAsync<List<ApprovalRequest>>($"{prefix}/approvals/pending");

    public async Task ApproveAsync(string prefix, string id, string? reason)
        => await _http.PostAsJsonAsync($"{prefix}/approvals/{id}/approve", new { reason });

    public async Task RejectAsync(string prefix, string id, string? reason)
        => await _http.PostAsJsonAsync($"{prefix}/approvals/{id}/reject", new { reason });

    // Escalations
    public async Task<List<EscalationRequest>> GetOpenEscalationsAsync(string prefix);
    public async Task<List<EscalationRequest>> GetRecentEscalationsAsync(string prefix, int count = 50);
    public async Task AcknowledgeEscalationAsync(string prefix, string id, string? agentId, string? humanAgent);
    public async Task ResolveEscalationAsync(string prefix, string id, string? reason);
    public async Task DismissEscalationAsync(string prefix, string id);
    public async Task ReplanEscalationAsync(string prefix, string id, string? note, string? requestedBy);

    // Chat sessions
    public async Task<List<ActiveChatSession>> GetActiveChatsAsync(string prefix);
    public async Task<List<ChatHistoryMessage>> GetSessionHistoryAsync(string prefix, string sessionId, int take = 50);
    public async Task<SentimentInfo> GetSentimentAsync(string prefix, string sessionId);
    public async Task TakeoverAsync(string prefix, string sessionId, string humanAgent);
    public async Task ReleaseAsync(string prefix, string sessionId);
    public async Task SendChatMessageAsync(string prefix, string sessionId, string text, string humanAgent);
    public async Task ReplanSessionAsync(string prefix, string sessionId, string? note, string? requestedBy);

    // Agents
    public async Task<List<AgentInfo>> GetAgentsAsync();

    // Improvements (lessons)
    public async Task<List<LessonProposal>> GetProposedLessonsAsync();
    public async Task<List<LessonProposal>> GetApprovedLessonsAsync();
    public async Task MineLessonsAsync();
    public async Task ApproveLessonAsync(string id, string? reason);
    public async Task RejectLessonAsync(string id, string? reason);
}
```

### Prefix kullanımı

```csharp
// Admin role'lü kullanıcı
var prefix = "";
var pending = await Admin.GetPendingApprovalsAsync(prefix);
// → GET /approvals/pending

// Agent role'lü kullanıcı
var prefix = "/agent";
var pending = await Admin.GetPendingApprovalsAsync(prefix);
// → GET /agent/approvals/pending
```

Server farklı endpoint set'leri sunar:
- `/approvals/*` (admin) — tüm pending'leri görür
- `/agent/approvals/*` — sadece kendine atananları görür

---

## ChatApiService

Müşteri chat akışı + rating + session metadata.

```csharp
public sealed class ChatApiService
{
    // Rating (anonim endpoint)
    public async Task SubmitRatingAsync(string sessionId, int stars, string? feedback)
        => await _http.PostAsJsonAsync($"/sessions/{sessionId}/rating", new { stars, feedback });

    public async Task<ConversationRating?> GetRatingAsync(string sessionId)
        => await _http.GetFromJsonAsync<ConversationRating>($"/sessions/{sessionId}/rating");

    // Session metadata
    public async Task<List<SessionSummary>> GetSessionsAsync()
        => await _http.GetFromJsonAsync<List<SessionSummary>>("/sessions/");

    public async Task<List<ConversationMessage>> GetSessionMessagesAsync(string sessionId)
        => await _http.GetFromJsonAsync<List<ConversationMessage>>($"/sessions/{sessionId}/messages");
}
```

`/chat/stream` SSE çağrısı **service'te değil** — JavaScript'te (`chat-bridge.js`). Service sadece JSON-based endpoint'ler.

---

## AnalyticsApiService

```csharp
public sealed class AnalyticsApiService
{
    public async Task<AnalyticsDashboard> GetDashboardAsync()
        => await _http.GetFromJsonAsync<AnalyticsDashboard>("/analytics/dashboard");

    public async Task<SessionAnalytics?> GetSessionAnalyticsAsync(string sessionId)
        => await _http.GetFromJsonAsync<SessionAnalytics>($"/analytics/session/{sessionId}");

    public async Task<List<ConversationRating>> GetRecentRatingsAsync(int count = 20)
        => await _http.GetFromJsonAsync<List<ConversationRating>>($"/analytics/ratings/recent?count={count}");
}
```

Admin paneli "Analytics" tab'ı bu service'i kullanır.

---

## TracesApiService

```csharp
public sealed class TracesApiService
{
    public async Task<List<TraceSession>> GetSessionsAsync()
        => await _http.GetFromJsonAsync<List<TraceSession>>("/traces/sessions");

    public async Task<TraceDetail?> GetTraceAsync(string traceId)
        => await _http.GetFromJsonAsync<TraceDetail>($"/traces/{traceId}");

    public async Task<List<TraceDetail>> GetBySessionAsync(string sessionId)
        => await _http.GetFromJsonAsync<List<TraceDetail>>($"/traces/by-session/{sessionId}");

    public async Task<List<TraceDetail>> GetRecentAsync(int count = 20)
        => await _http.GetFromJsonAsync<List<TraceDetail>>($"/traces/recent?count={count}");

    // Bridge history — escalation transcript modal için
    public async Task<List<ChatHistoryMessage>> GetBridgeHistoryAsync(string sessionId, int take = 50);

    // Session detay — admin paneli için
    public async Task<List<ApprovalRequest>> GetApprovalsBySessionAsync(string sessionId);
    public async Task<List<EscalationRequest>> GetEscalationsBySessionAsync(string sessionId);
}
```

`TraceDetail` modeli ([Models.md](Models.md)) trace'in tam içeriğini taşır — reasoning, planning, agent visits, tool calls.

---

## SlaApiService

```csharp
public sealed class SlaApiService
{
    public async Task<SlaStatus> GetStatusAsync()
        => await _http.GetFromJsonAsync<SlaStatus>("/sla/status");

    public async Task<List<SlaEvent>> GetEventsAsync(int count = 50)
        => await _http.GetFromJsonAsync<List<SlaEvent>>($"/sla/events?count={count}");
}
```

`Sla.razor` 5 saniyede bir polling — bu service'i kullanır.

---

## WorkflowApiService

```csharp
public sealed class WorkflowApiService
{
    public async Task<List<WorkflowSummary>> GetListAsync()
    {
        var resp = await _http.GetFromJsonAsync<WorkflowListResponse>("/workflows");
        return resp?.Items ?? new();
    }

    // GetRawAsync — JSON'u pretty-print formatla
    public async Task<string> GetRawAsync(string id)
    {
        var json = await _http.GetStringAsync($"/workflows/{id}");
        return PrettyPrint(json);
    }

    public async Task SaveAsync(string? id, string rawJson)
    {
        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");
        if (string.IsNullOrEmpty(id))
            await _http.PostAsync("/workflows", content);
        else
            await _http.PutAsync($"/workflows/{id}", content);
    }

    public async Task DeleteAsync(string id)
        => await _http.DeleteAsync($"/workflows/{id}");

    public async Task<WorkflowExecutionResult> TestAsync(string id, string input, Dictionary<string, string>? variables)
    {
        var resp = await _http.PostAsJsonAsync($"/workflows/{id}/test", new { input, variables });
        return await resp.Content.ReadFromJsonAsync<WorkflowExecutionResult>() ?? throw new Exception("Boş yanıt");
    }

    private static string PrettyPrint(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }
}
```

### Neden raw JSON?

WorkflowDesigner JSON editor kullanıyor — kullanıcı yazdığı stringi olduğu gibi POST/PUT etmek istiyoruz. Strongly-typed DTO'ya parse etmek:
- Schema değişikliklerine karşı kırılgan
- Editor'da ek bilgileri (yorum, formatlama) kaybeder
- "Server side ne kabul ederse al" pattern'i daha esnek

PrettyPrint olarak okur — kullanıcı düzenler — raw string olarak yazar.

---

## Exception handling pattern

Service'ler exception fırlatır — sayfa katmanı yakalar:

```csharp
// Razor sayfada
private async Task LoadAsync()
{
    try
    {
        _data = await Admin.GetPendingApprovalsAsync(_apiPrefix);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
    {
        // Token expired veya geçersiz
        var refreshed = await Auth.TryRefreshAsync();
        if (refreshed != null) await LoadAsync();   // Retry
        else Nav.NavigateTo("/login");
    }
    catch (Exception ex)
    {
        _error = ex.Message;
    }
    finally
    {
        _loading = false;
    }
}
```

Bu pattern her sayfada tekrarlanır. Centralized 401 handling için custom DelegatingHandler eklenebilir ama şimdilik per-page tercih edildi (görünür ve esnek).

---

## Cancellation token kullanımı

Çoğu service method **CancellationToken parametresi almıyor** — Blazor WASM tek-thread olduğu için iptal pratik değil. Sayfa unload olunca:
- HttpClient otomatik dispose olur
- Pending request'ler implicit cancel olur

İhtiyaç olursa eklenir:

```csharp
public async Task<List<X>> GetAsync(CancellationToken ct = default)
    => await _http.GetFromJsonAsync<List<X>>("/endpoint", ct);
```

---

## Bağlantılar

- [Auth.md](Auth.md) — AuthorizedHttpClientHandler nasıl token ekler
- [Api projesi](../api/README.md) — server tarafı endpoint dokümantasyonu
- [Models.md](Models.md) — service'lerin döndürdüğü tipler
