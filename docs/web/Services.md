# API Service'leri

**Klasör:** `Services/`

API endpoint'lerini wrap eden client sınıfları ve UI yardımcı servisleri. Sayfalar (Razor) bunları `@inject` ile alır, doğrudan `HttpClient` kullanmaz.

| Service | Endpoint prefix | Kullanım |
|---|---|---|
| `AdminApiService` | `/approvals`, `/escalations`, `/chat-sessions`, `/improvements`, `/agents`, `/sessions` | Admin panel |
| `AnalyticsApiService` | `/analytics` | Admin panel (Analytics tab) |
| `ChatApiService` | `/chat`, `/sessions` | Chat sayfası |
| `TracesApiService` | `/traces`, `/chat-sessions/.../history`, `/approvals`, `/escalations` | Traces, Replay |
| `SlaApiService` | `/sla` | SLA sayfası |
| `WorkflowApiService` | `/workflows` | WorkflowDesigner |
| `ThemeService` | — (localStorage / DOM) | Karanlık/aydınlık mod yönetimi |

`AuthorizedHttpClientHandler` JWT token otomatik inject eder.
`AuthService` hariç — refresh döngüsünü önlemek için ham `HttpClient` kullanır.

---

## ThemeService

**Dosya:** `Services/ThemeService.cs`  
**Tür:** Scoped

Kullanıcı tema tercihini yönetir. `localStorage`'da saklar, sayfalar arası tutarlılığı sağlar.

```csharp
public sealed class ThemeService
{
    public bool IsDark { get; private set; }
    public event Action? OnChange;

    public async ValueTask EnsureInitAsync()   // İlk render'da mevcut temayı okur
    public async Task ToggleAsync()            // dark ↔ light geçiş, event fırlatır
}
```

### Çalışma prensibi

1. `index.html` `<head>`'inde inline script, sayfa yüklenir yüklenmez `data-theme` attribute'unu ayarlar (FOUC önleme):

```html
<script>
(function () {
    var s = localStorage.getItem('csb-theme');
    var d = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    document.documentElement.setAttribute('data-theme', s || (d ? 'dark' : 'light'));
})();
</script>
```

2. CSS `[data-theme="dark"]` seçicisi tüm dark mode overrides'ları tanımlar (`styles.css`).

3. `ThemeService.ToggleAsync()` → `window.csbTheme.toggle()` JS çağrısı → `<html data-theme>` değiştirilir + `localStorage` güncellenir.

### JS API (`index.html`)

```javascript
window.csbTheme = {
    get()    { return document.documentElement.getAttribute('data-theme') || 'light'; },
    toggle() {
        var next = ... === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('csb-theme', next);
        return next;
    }
};
```

### Bileşenlerde kullanım

```razor
@inject ThemeService Theme

<button @onclick="ToggleTheme">
    @(Theme.IsDark ? "☀" : "☾")
</button>

@code {
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Theme.EnsureInitAsync();
        Theme.OnChange += StateHasChanged;
    }

    private async Task ToggleTheme() => await Theme.ToggleAsync();
}
```

Toggle butonu `NavMenu.razor`, `AdminNavBar.razor` ve Chat sayfasında mevcuttur.

---

## AdminApiService

Admin panelinin tüm endpoint çağrıları. **Role-aware prefix** — `AppAuthStateProvider`'dan role okuyarak `/agent` veya `""` prefix seçer:

```csharp
private async Task<string> PrefixAsync()
{
    var state = await authState.GetAuthenticationStateAsync();
    var role = state.User.FindFirst(ClaimTypes.Role)?.Value;
    return role == "Agent" ? "/agent" : string.Empty;
}
```

### Approvals

```csharp
Task<List<ApprovalRequest>> GetPendingApprovalsAsync()
    // → GET {prefix}/approvals/pending

Task<List<ApprovalRequest>> GetRecentApprovalsAsync(int count = 50)
    // → GET {prefix}/approvals/recent?count={count}

Task ApproveAsync(string id, string? reason, string decidedBy = "admin")
    // → POST {prefix}/approvals/{id}/approve

Task RejectAsync(string id, string? reason, string decidedBy = "admin")
    // → POST {prefix}/approvals/{id}/reject
```

### Escalations

```csharp
Task<List<EscalationRequest>> GetOpenEscalationsAsync()
    // → GET {prefix}/escalations/open

Task<List<EscalationRequest>> GetRecentEscalationsAsync(int count = 50)
    // → GET {prefix}/escalations/recent?count={count}

Task AcknowledgeAsync(string id, string assignedTo)
    // → POST {prefix}/escalations/{id}/acknowledge

Task ResolveEscalationAsync(string id, string resolution, string assignedTo = "admin")
    // → POST {prefix}/escalations/{id}/resolve

Task DismissAsync(string id, string resolution)
    // → POST {prefix}/escalations/{id}/dismiss

Task ReplanEscalationAsync(string id, string? note, string requestedBy = "admin")
    // → POST {prefix}/escalations/{id}/replan
```

### Chat Sessions

```csharp
Task<List<ActiveChatSession>> GetActiveChatsAsync()
    // → GET {prefix}/chat-sessions/active

Task<List<ChatHistoryMessage>> GetChatHistoryAsync(string sessionId, int take = 200)
    // → GET {prefix}/chat-sessions/{sessionId}/history?take={take}

Task TakeoverAsync(string sessionId, string humanAgent)
    // → POST {prefix}/chat-sessions/{sessionId}/takeover

Task ReleaseAsync(string sessionId)
    // → POST {prefix}/chat-sessions/{sessionId}/release

Task SendChatMessageAsync(string sessionId, string text)
    // → POST {prefix}/chat-sessions/{sessionId}/messages

Task ReplanChatAsync(string sessionId, string? note = null, string requestedBy = "admin")
    // → POST {prefix}/chat-sessions/{sessionId}/replan

Task<ChatSentiment?> GetSentimentAsync(string sessionId)
    // → GET {prefix}/chat-sessions/{sessionId}/sentiment
```

### Agents

```csharp
Task<List<AgentInfo>> GetAgentsAsync()
    // → GET /agents (merged list)
```

`AgentListResponse(int Count, List<AgentInfo> Items)` ile deserialize edilir.

### Sessions

```csharp
Task<List<SessionSummary>> GetSessionsAsync()
    // → GET /sessions/
```

### Improvements (Lessons)

```csharp
Task<List<LessonProposal>> GetLessonsAsync(string status)
    // → GET /improvements?status={status}

Task<(int Candidates, int Proposed, string? Error)> MineImprovementsAsync()
    // → POST /improvements/mine

Task ApproveLessonAsync(string id, string? reason = null)
    // → POST /improvements/{id}/approve

Task RejectLessonAsync(string id, string? reason = null)
    // → POST /improvements/{id}/reject
```

`MineImprovementsAsync` yanıt JSON'undan `candidates`, `proposedLessons`, `error` field'larını okur.

### Yardımcı tip

```csharp
public sealed record ChatSentiment(string? Sentiment, double Score);
```

---

## AnalyticsApiService

```csharp
public sealed class AnalyticsApiService(HttpClient http)
```

```csharp
Task<AnalyticsDashboard?> GetDashboardAsync()
    // → GET /analytics/dashboard

Task<SessionAnalyticsModel?> GetSessionAnalyticsAsync(string sessionId)
    // → GET /analytics/session/{sessionId}
```

Admin paneli "Analytics" tab'ı bu service'i kullanır. Hata durumunda `null` döner.

---

## ChatApiService

```csharp
public sealed class ChatApiService(HttpClient http)
```

```csharp
Task<RatingResponse?> SubmitRatingAsync(string sessionId, int stars, string? feedback)
    // → POST /sessions/{sessionId}/rating

Task<RatingResponse?> GetRatingAsync(string sessionId)
    // → GET /sessions/{sessionId}/rating

Task<List<SessionInfo>> GetSessionsAsync()
    // → GET /sessions/

Task<List<SessionMessage>> GetSessionMessagesAsync(string sessionId)
    // → GET /sessions/{sessionId}/messages
```

### Yardımcı tipler

```csharp
public sealed record RatingResponse(int Stars, string? Feedback);
public sealed record SessionInfo(string SessionId, DateTimeOffset LastActivity, int MessageCount);
public sealed record SessionMessage(string Role, string Content, DateTimeOffset Timestamp);
```

`/chat/stream` SSE çağrısı **service'te değil** — JavaScript'te (`chat-bridge.js`). Service sadece JSON-based endpoint'ler.

---

## TracesApiService

```csharp
public sealed class TracesApiService(HttpClient http)
```

Trace dashboard API servisi. `/traces/sessions` ve `/traces/{traceId}` endpoint'lerinin Blazor karşılığı.

### Lokal tipler (dosya içinde)

```csharp
public sealed record TraceSession(
    string SessionId, string? Title, int TraceCount, int MessageCount, DateTimeOffset LastTraceAt);

public sealed record SessionChatMessage(string Role, string Text);

public sealed record BridgeChatMessage(
    string Sender, string Text, string? HumanAgent, DateTime Timestamp);
```

### Metodlar

```csharp
Task<List<TraceSession>> GetSessionsAsync()
    // → GET /traces/sessions

Task<TraceDetail?> GetTraceAsync(string traceId)
    // → GET /traces/{traceId} (URI encoded)

Task<List<TraceDetail>> GetBySessionAsync(string sessionId)
    // → GET /traces/by-session/{sessionId}

Task<List<TraceDetail>> GetRecentAsync(int count = 50)
    // → GET /traces/recent?count={count}

Task<List<BridgeChatMessage>> GetBridgeHistoryAsync(string sessionId, int take = 100)
    // → GET /chat-sessions/{sessionId}/history?take={take}

Task<List<ApprovalRequest>> GetApprovalsBySessionAsync(string sessionId)
    // → GET /approvals/recent?count=200 + filter by SessionId

Task<List<EscalationRequest>> GetEscalationsBySessionAsync(string sessionId)
    // → GET /escalations/recent?count=200 + filter by SessionId
```

`ApprovalRequest` ve `EscalationRequest` için `AdminModels.cs` tiplerini kullanır.

---

## SlaApiService

```csharp
public sealed class SlaApiService(HttpClient http)
```

```csharp
Task<SlaStatus?> GetStatusAsync()
    // → GET /sla/status

Task<List<SlaEvent>> GetEventsAsync(int count = 100)
    // → GET /sla/events?count={count}
```

`SlaEventsResponse(int TotalCount, List<SlaEvent> Items)` ile deserialize edilir, `Items` döndürülür. Hata durumunda `null`/`[]` döner.

---

## WorkflowApiService

```csharp
public sealed class WorkflowApiService(HttpClient http)
```

```csharp
Task<List<WorkflowListEntry>> GetListAsync()
    // → GET /workflows (WorkflowListResponse → Items)

Task<string?> GetRawAsync(string id)
    // → GET /workflows/{id} → JsonElement pretty-print

Task<(bool Ok, string? Error, string? SavedId)> SaveAsync(string? id, string rawJson)
    // id=null → POST /workflows
    // id=... → PUT /workflows/{id}

Task<(bool Ok, string? Error)> DeleteAsync(string id)
    // → DELETE /workflows/{id}

Task<string> TestAsync(string id, string input)
    // → POST /workflows/{id}/test
    //   body: { input }
    //   Response: JsonElement → pretty-print string
```

### Neden raw JSON?

WorkflowDesigner JSON editor kullanıyor. `SaveAsync` raw string'i `JsonSerializer.Deserialize<JsonElement>` ile parse eder, `JsonContent.Create(body)` ile gönderir. Parse hatası → `(false, "JSON hatası: ...", null)` döner.

### Pretty-print ayarı

```csharp
private static readonly JsonSerializerOptions _pretty = new()
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};
```

`GetRawAsync` ve `TestAsync` bu ayarla format eder.

---

## AuthService

`Services/AuthService.cs` — Backend `/auth` endpoint'leriyle iletişim.

```csharp
public sealed class AuthService(HttpClient http, AuthTokenStore store)
```

```csharp
Task<AuthTokenData> LoginAsync(string username, string password)
    // → POST /auth/login
    // Başarılı: AuthTokenStore.WriteAsync(data)
    // Başarısız: InvalidOperationException fırlatır

Task LogoutAsync()
    // → POST /auth/logout (refresh token ile)
    // AuthTokenStore.WriteAsync(null)

Task<AuthTokenData?> TryRefreshAsync()
    // → POST /auth/refresh
    // Parallel çağrıları birleştirir (_refreshTask)
    // Başarısız: store null, null döner
```

**Refresh parallel coalescing:**

```csharp
private Task? _refreshTask;

public async Task<AuthTokenData?> TryRefreshAsync()
{
    var current = await store.ReadAsync();
    if (current?.RefreshToken is null) return null;

    if (_refreshTask is not null)
    {
        await _refreshTask;
        return await store.ReadAsync();
    }

    var tcs = new TaskCompletionSource();
    _refreshTask = tcs.Task;
    // ... DoRefreshAsync
    finally { _refreshTask = null; tcs.SetResult(); }
}
```

Birden fazla 401 aynı anda gelirse tek refresh request gönderilir.

---

## AuthTokenStore

`Services/AuthTokenStore.cs` — localStorage token yönetimi.

```csharp
public sealed class AuthTokenStore(IJSRuntime js)
```

| Metod | İşlem |
|---|---|
| `ReadAsync()` | `localStorage.getItem("cs.auth")` → `AuthTokenData?` |
| `WriteAsync(data)` | data≠null → `localStorage.setItem`; null → `localStorage.removeItem` |
| `GetAccessTokenAsync()` | `ReadAsync()?.AccessToken` |

```csharp
public sealed record AuthTokenData(
    string AccessToken, string RefreshToken, string Username, string Role);
```

JSON camelCase policy ile serialize/deserialize edilir.

---

## AppAuthStateProvider

`Services/AppAuthStateProvider.cs` — Blazor auth state.

```csharp
public sealed class AppAuthStateProvider(AuthTokenStore store) : AuthenticationStateProvider
```

```csharp
public override async Task<AuthenticationState> GetAuthenticationStateAsync()
{
    var token = await store.ReadAsync();
    if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
        return Anonymous;

    var identity = new ClaimsIdentity(
    [
        new Claim(ClaimTypes.Name, token.Username),
        new Claim(ClaimTypes.Role, token.Role)
    ], "jwt");

    return new AuthenticationState(new ClaimsPrincipal(identity));
}

public void NotifyStateChanged()
    => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
```

JWT decode yapmaz — login response'taki `Username`/`Role` field'larını kullanır. `NotifyStateChanged` → `CascadingAuthenticationState` tüm component'lere yeni state yayar.

---

## AuthorizedHttpClientHandler

`Services/AuthorizedHttpClientHandler.cs` — DelegatingHandler.

```csharp
public sealed class AuthorizedHttpClientHandler(
    AuthTokenStore store, AuthService authService,
    NavigationManager nav, AppAuthStateProvider authState) : DelegatingHandler
```

**Akış:**

1. `/auth/*` endpoint'lerine token ekleme (refresh döngüsünü önler)
2. `store.GetAccessTokenAsync()` → `Authorization: Bearer <token>`
3. Response 401 değilse döner
4. 401 → `authService.TryRefreshAsync()`
5. Refresh başarısızsa `authState.NotifyStateChanged()` + `/login?return=...` yönlendirme
6. Refresh başarılıysa request clone'lanır, yeni token ile tekrar gönderilir

---

## Bağlantılar

- [Auth.md](Auth.md) — auth bileşenleri detayı
- [Api projesi](../api/README.md) — server tarafı endpoint dokümantasyonu
- [Models.md](Models.md) — service'lerin döndürdüğü tipler
