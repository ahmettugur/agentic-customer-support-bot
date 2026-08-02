# Admin Page — HITL Paneli

**Dosya:** `Pages/Admin.razor`  
**Route:** `/admin`  
**Layout:** `AdminLayout`  
**Auth:** Admin veya Agent

Web projesinin **en kapsamlı sayfası** — tüm HITL operasyonları, analytics ve self-improvement burada.

---

## Tab yapısı

| Tab | İçerik |
|---|---|
| **Approvals** | Bekleyen tool onayları |
| **Escalations** | Açık + kapalı escalation'lar (sidebar split) |
| **Active Chats** | Canlı human takeover oturumları |
| **History** | Son 50 onay + son 50 escalation karar |
| **Analytics** | Dashboard chart'ları, rating dağılımı, sentiment |
| **Improvements** | LessonMiner önerileri (admin onay/red) |

Her tab kendi state ve refresh logic'i içeriyor.

---

## Role-aware endpoint prefix

```csharp
private string _apiPrefix = "";   // Admin → "" (e.g., "/approvals/pending")
                                   // Agent → "/agent" (e.g., "/agent/approvals/pending")

protected override async Task OnInitializedAsync()
{
    var authState = await AuthStateProvider.GetAuthenticationStateAsync();
    var role = authState.User.FindFirst(ClaimTypes.Role)?.Value;
    _apiPrefix = role == "Agent" ? "/agent" : "";
}
```

Her API çağrısı:
```csharp
await Admin.GetPendingApprovalsAsync(_apiPrefix);
// Admin için: GET /approvals/pending
// Agent için: GET /agent/approvals/pending
```

Server-side ayrı endpoint set'leri agent'a filtreli veri döner.

---

## Approval workflow

### Pending listesi

```razor
@foreach (var app in _approvals)
{
    <div class="approval-card">
        <div class="header">
            <span class="tool-name">@app.ToolName</span>
            <span class="session-id">@app.SessionId</span>
        </div>
        <div class="user-query">"@app.UserQuery"</div>
        <div class="params"><pre>@FormatParams(app.Parameters)</pre></div>
        <div class="actions">
            <button @onclick="() => ApproveAsync(app)" class="btn-approve">Onayla</button>
            <button @onclick="() => RejectAsync(app)" class="btn-reject">Reddet</button>
        </div>
    </div>
}
```

### High-risk tool — reason zorunlu

```csharp
private static readonly HashSet<string> HighRiskTools = new()
{
    "order_placement_tool",
    "complaint_registration_tool"
};

private async Task ApproveAsync(ApprovalRequest app)
{
    string? reason = null;
    if (HighRiskTools.Contains(app.ToolName))
    {
        reason = await PromptModalAsync("Onay gerekçesi (zorunlu):");
        if (string.IsNullOrWhiteSpace(reason))
        {
            // Reason zorunlu, modal cancel edilmiş
            return;
        }
    }

    await Admin.ApproveAsync(_apiPrefix, app.Id, reason);
    await RefreshApprovalsAsync();
}
```

`PromptModalAsync` reusable modal — text input + OK/Cancel.

---

## Escalation workflow

### Open vs Closed split

```razor
<div class="escalations-container">
    <!-- Açık escalation'lar (geniş) -->
    <div class="open-pane">
        @foreach (var esc in _openEscalations)
        {
            <EscalationCard Escalation="esc" OnAction="HandleEscalationAction" />
        }
    </div>

    <!-- Kapalı escalation'lar (dar sidebar) -->
    <div class="closed-pane">
        <h3>Kapalı</h3>
        @foreach (var esc in _closedEscalations)
        {
            <div class="closed-item" @onclick="() => ShowTranscriptAsync(esc)">
                <span class="replan-marker" hidden="@(!esc.RoutingNote?.Contains("replan") == true)">🔄</span>
                <span>@esc.Reason</span>
                <span class="status">@esc.Status</span>
            </div>
        }
    </div>
</div>
```

### Aksiyonlar

| Aksiyon | Endpoint | Davranış |
|---|---|---|
| **Atama Yap** | `POST /escalations/{id}/acknowledge` | Modal: agent seç, atama yap |
| **Devral & Sohbet** | (combo) `acknowledge` + `chat-sessions/.../takeover` | Atama yap + chat moduna geç + session aç |
| **Yeniden Planla** | `POST /escalations/{id}/replan` | Resolve + Release + bot replan tetikle |
| **Çöz** | `POST /escalations/{id}/resolve` | Resolution metni iste |
| **Dismiss** | `POST /escalations/{id}/dismiss` | False positive olarak kapat |

### Assign modal

```razor
<dialog id="assignModal">
    <h3>Eskalasyonu Bir Temsilciye Ata</h3>
    <select @bind="_selectedAgentId">
        <option value="">— Liste —</option>
        @foreach (var agent in _availableAgents)
        {
            <option value="@agent.Id">@agent.DisplayName (@agent.CurrentLoad/@agent.MaxConcurrentLoad)</option>
        }
    </select>
    <!-- Liste'de yoksa manuel ID girişi -->
    <input @bind="_freeTextAgentId" placeholder="Veya agent ID gir..." />

    <button @onclick="ConfirmAssignAsync">Ata</button>
    <button @onclick="CloseAssignModal">İptal</button>
</dialog>
```

Yük dağılımı UI: `currentLoad / maxConcurrentLoad` chip — admin agent doluluğunu görür.

---

## Active Chats panel

Sol: aktif session listesi. Sağ: seçili session'ın canlı görünümü.

### Session listesi

```razor
@foreach (var sess in _activeSessions)
{
    <div class="session-item @(sess.SessionId == _selectedSessionId ? "selected" : "")"
         @onclick="() => SelectSessionAsync(sess.SessionId)">
        <div>👤 @sess.HumanAgent</div>
        <small>@sess.MessageCount mesaj · @FormatTime(sess.EnteredAt)</small>
    </div>
}
```

### Seçili session detayı

```razor
@if (_selectedSession != null)
{
    <div class="chat-panel">
        <!-- Sentiment badge -->
        <div class="sentiment-badge @SentimentClass(_currentSentiment.Score)">
            @SentimentLabel(_currentSentiment.Score) (@_currentSentiment.Score.ToString("F2"))
        </div>

        <!-- Mesaj geçmişi -->
        <div class="messages" id="adminMessages">
            @foreach (var msg in _chatHistory)
            {
                <div class="msg msg-@msg.Sender">
                    @if (msg.Sender == "system" && msg.Text.Contains("yeniden planlama"))
                    {
                        <div class="internal-note">📋 @msg.Text</div>
                    }
                    else
                    {
                        <span>@msg.Text</span>
                    }
                    <small>@FormatTime(msg.At)</small>
                </div>
            }
        </div>

        <!-- Composer -->
        <div class="composer">
            <textarea @bind="_adminInput" placeholder="Yanıtınızı yazın..."></textarea>
            <button @onclick="SendAdminMessageAsync">Gönder</button>
        </div>

        <!-- Aksiyonlar -->
        <div class="session-actions">
            <button @onclick="ReplanCurrentSessionAsync">Yeniden Planla</button>
            <button @onclick="EndChatAsync">Sohbeti Bitir</button>
        </div>
    </div>
}
```

### SSE bridge — admin-chat-bridge.js

```csharp
private async Task SelectSessionAsync(string sessionId)
{
    _selectedSessionId = sessionId;
    _chatHistory = await Admin.GetSessionHistoryAsync(_apiPrefix, sessionId);

    // SSE abonelik başlat
    await JS.InvokeVoidAsync("__adminChatSetup", _accessToken, _apiBaseUrl);
    await JS.InvokeVoidAsync("__adminSubscribeChat", _dotNetRef, sessionId);

    // Sentiment timer
    _sentimentTimer?.Dispose();
    _sentimentTimer = new Timer(async _ => await RefreshSentimentAsync(), null, 0, 5000);
}

[JSInvokable]
public async Task OnChatEvent(string type, string data)
{
    if (type == "bridge_message")
    {
        var msg = JsonSerializer.Deserialize<ChatHistoryMessage>(data);
        _chatHistory.Add(msg);
        await ScrollToBottomAsync("adminMessages");
        StateHasChanged();
    }
}
```

Detay: [JsInterop.md](JsInterop.md).

---

## Auto-refresh

```csharp
private Timer? _refreshTimer;
private bool _autoRefreshEnabled = true;

protected override void OnInitialized()
{
    _refreshTimer = new Timer(async _ =>
    {
        if (_autoRefreshEnabled)
        {
            await RefreshBadgesAsync();       // Tüm tab'ların badge sayıları
            await RefreshActiveTabAsync();    // Aktif tab içeriği
            await InvokeAsync(StateHasChanged);
        }
    }, null, 3000, 3000);
}
```

15 saniyede bir yenileme (`AutoRefreshInterval`). Eskiden 3 saniyeydi ve panel dakikada ~100 istek üretip sunucudaki 60/dk'lık `general` rate limit'ini kendi tüketiyordu; manuel işlemler HTTP 429 alıyordu. Badge'ler: kaç pending approval, kaç open escalation vb.

UI'da toggle:

```razor
<label>
    <input type="checkbox" @bind="_autoRefreshEnabled" />
    Otomatik yenile
</label>
```

---

## Analytics tab

```razor
<div class="analytics-grid">
    <div class="metric-card">
        <span class="label">Toplam Oturum</span>
        <span class="value">@_dashboard.TotalSessions</span>
    </div>
    <div class="metric-card">
        <span class="label">Ortalama Puan</span>
        <span class="value">@_dashboard.AverageRating.ToString("F1")</span>
    </div>
    <!-- vb. -->
</div>

<div class="charts">
    <BarChart Title="Puan Dağılımı" Data="@_dashboard.RatingDistribution" />
    <BarChart Title="Sentiment Dağılımı" Data="@_dashboard.SentimentDistribution" />
    <BarChart Title="Intent Dağılımı" Data="@_dashboard.IntentDistribution" />
    <BarChart Title="Faz Dağılımı" Data="@_dashboard.PhaseDistribution" />
</div>

<table class="recent-ratings">
    <thead><tr><th>Session</th><th>Yıldız</th><th>Geri Bildirim</th></tr></thead>
    <tbody>
        @foreach (var r in _recentRatings)
        {
            <tr>
                <td>@r.SessionId</td>
                <td>@new string('⭐', r.Stars)</td>
                <td>@r.Feedback</td>
            </tr>
        }
    </tbody>
</table>
```

Detay: [Api Endpoints-Observability](../api/Endpoints-Observability.md).

---

## Improvements tab

Self-improvement loop UI'ı.

```razor
<button @onclick="MineLessonsAsync">⛏️ Şimdi Lesson Mine Et</button>

<div class="proposed-lessons">
    <h3>Önerilen Lesson'lar</h3>
    @foreach (var lesson in _proposedLessons)
    {
        <div class="lesson-card">
            <h4>@lesson.Title</h4>
            <p class="text">@lesson.LessonText</p>
            <small class="observation">📝 @lesson.Observation</small>
            <div class="source-traces">
                @foreach (var traceId in lesson.SourceTraceIds)
                {
                    <a href="/replay?traceId=@traceId" target="_blank">@traceId</a>
                }
            </div>
            <div class="actions">
                <button @onclick="() => ApproveLessonAsync(lesson)">Onayla</button>
                <button @onclick="() => RejectLessonAsync(lesson)">Reddet</button>
            </div>
        </div>
    }
</div>

<div class="approved-lessons">
    <h3>Onaylanmış</h3>
    @foreach (var lesson in _approvedLessons)
    {
        <div class="lesson-card approved">
            <h4>✅ @lesson.Title</h4>
            <small>@lesson.DecidedBy · @FormatTime(lesson.DecidedAt) · @lesson.DecisionReason</small>
        </div>
    }
</div>
```

`Source traces` linkleri → `/replay?traceId=...` ile o trace'i step-by-step incele.

---

## Modals

| Modal | Kullanım |
|---|---|
| **Prompt Modal** | Reason/note girişi |
| **Assign Modal** | Agent seç + manuel ID |
| **Transcript Modal** | Kapalı escalation'ın chat history'sini gör |

Tümü `<dialog>` HTML element'i — Blazor JS interop ile `showModal()` ve `close()`.

---

## Bağlantılar

- [JsInterop.md](JsInterop.md) — admin-chat-bridge.js
- [Api Endpoints-Admin](../api/Endpoints-Admin.md) — server tarafı
- [Api Endpoints-Observability](../api/Endpoints-Observability.md) — analytics endpoints
- [Services.md](Services.md) — AdminApiService, AnalyticsApiService
- [Models.md](Models.md) — AdminModels.cs (ApprovalRequest, EscalationRequest, vb.)
