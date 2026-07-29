# Observability Sayfaları

**Dosyalar:**
- `Pages/Traces.razor` — `/traces`
- `Pages/Replay.razor` — `/replay`
- `Pages/Sla.razor` — `/sla`
- `Components/TraceDetailPanel.razor`
- `Components/TraceSessionItem.razor`

Reasoning trace audit, step-by-step replay ve SLA dashboard.

---

## Traces — `/traces`

3-pane layout: sol session listesi, orta trace listesi, sağ detay paneli.

### Sayfa state

```csharp
private List<TraceSession> _sessions = new();
private string? _selectedSessionId;
private List<TraceDetail> _traces = new();
private string? _selectedTraceId;
private TraceDetail? _selectedTrace;
private bool _autoRefresh = true;
```

> Gerçek alan adları `_selectedTrace` (`_currentTrace` değil) ve `_autoRefresh` varsayılanı **`true`**'dur (`false` değil).

### Layout

```razor
<div class="traces-layout">
    <!-- Sol: session sidebar -->
    <aside class="sessions-panel">
        <div class="header">
            <h3>Oturumlar</h3>
            <button @onclick="ShowAllTracesAsync">Tümü</button>
            <label><input type="checkbox" @bind="_autoRefresh" />Yenile</label>
        </div>
        @foreach (var sess in _sessions)
        {
            <TraceSessionItem
                Session="sess"
                IsActive="@(sess.SessionId == _selectedSessionId)"
                OnSessionSelected="SelectSessionAsync" />
        }
    </aside>

    <!-- Orta: trace listesi -->
    <main class="traces-panel">
        @foreach (var trace in _traces)
        {
            <div class="trace-item @TraceClass(trace) @(trace.TraceId == _selectedTraceId ? "selected" : "")"
                 @onclick="() => SelectTraceAsync(trace.TraceId)">
                <span class="trace-id">@trace.TraceId</span>
                <span class="time">@FormatTime(trace.StartedAt)</span>
                <span class="duration">@trace.DurationMs ms</span>
                @if (!string.IsNullOrEmpty(trace.Error))
                {
                    <span class="error-badge">❌</span>
                }
            </div>
        }
    </main>

    <!-- Sağ: detay paneli -->
    <aside class="detail-panel">
        @if (_selectedTrace != null)
        {
            <TraceDetailPanel Trace="_selectedTrace" />
        }
    </aside>
</div>
```

### Akış

```
SelectSessionAsync(sid)
   ↓ TracesApi.GetBySessionAsync(sid)
_traces = [trace1, trace2, ...]
   ↓
SelectTraceAsync(traceId)
   ↓ TracesApi.GetTraceAsync(traceId)
_selectedTrace = TraceDetail { ... full data ... }
```

`ShowAllTracesAsync` session filter'sız tüm trace'leri getirir (son N).

---

## TraceSessionItem component

```razor
<div class="session-item @(IsActive ? "active" : "")"
     @onclick="OnClick" role="button" tabindex="0" @onkeydown="HandleKeyDown">
    <div class="session-item-title">@(Session.Title ?? "(boş)")</div>
    <div class="session-item-meta">
        <span>@Session.TraceCount trace</span>
        <span>@Session.MessageCount mesaj</span>
        <span>@FormatTime(Session.LastTraceAt)</span>
    </div>
    <code class="session-item-id">@Session.SessionId[..Math.Min(8, Session.SessionId.Length)]…</code>
</div>

@code {
    [Parameter, EditorRequired] public TraceSession Session { get; set; } = default!;
    [Parameter] public bool IsActive { get; set; }
    [Parameter] public EventCallback<string> OnSessionSelected { get; set; }

    private async Task OnClick() => await OnSessionSelected.InvokeAsync(Session.SessionId);

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
            await OnSessionSelected.InvokeAsync(Session.SessionId);
    }
}
```

Parametre adları **`IsActive`** (`Selected` değil) ve **`OnSessionSelected` (`EventCallback<string>`, seçilen `SessionId`'yi taşır)** — parametresiz `EventCallback OnClick` değil. Klavye erişilebilirliği için `role="button"`/`@onkeydown` de eklenmiştir.

---

## TraceDetailPanel component

Trace'in tüm detayını gösterir — reasoning, planning, agent timeline, tool çağrıları, chat/approval/escalation geçmişi, specialist reasoning'ler, final critique/revision.

### Gerçek `TraceDetail` modeli

`Reasoning` ve `Planning` **tipli sınıflar değil, ham `JsonElement?`'tir** — API'nin döndürdüğü JSON aynen taşınır, Web tarafında ayrı bir DTO'ya deserialize edilmez:

```csharp
public sealed class TraceDetail
{
    public string? TraceId { get; init; }
    public string? SessionId { get; init; }
    public string? UserQuery { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? TerminationReason { get; init; }
    public int? DurationMs { get; init; }
    public int? IterationCount { get; init; }
    public string? Error { get; init; }
    public string? FinalResponse { get; init; }
    public JsonElement? Reasoning { get; init; }        // ham JSON — tipli değil
    public JsonElement? Planning { get; init; }          // ham JSON — tipli değil
    public List<TraceAgentVisit> AgentVisits { get; init; } = [];
    public List<JsonElement> SpecialistReasonings { get; init; } = [];
    public List<TraceToolCall> ToolCalls { get; init; } = [];
    public bool WasRevised { get; init; }
    public string? FirstDraftResponse { get; init; }
    public JsonElement? FinalCritique { get; init; }
}

public sealed record TraceAgentVisit(string? AgentName, DateTimeOffset StartedAt, int? DurationMs, string? Output);
public sealed record TraceToolCall(string? ToolName, string? AgentName, DateTimeOffset? InvokedAt, bool Success, string? ParametersSummary, string? ResultSummary);
```

> `EstimatedTokens`, `ReasoningSummary`, `PlanningSummary` gibi tipler **kodda hiç mevcut değildir** — Model dosyası için bkz. [Models.md](Models.md), o zaten bu gerçek şemayı doğru dokümante ediyor.

### Basitleştirilmiş görünüm

`Reasoning`/`Planning` ham `JsonElement` olduğu için component `@Trace.Reasoning.Analysis` gibi doğrudan property erişimi **yapamaz** — `RenderReasoning`/`RenderPlanning` gibi manuel JSON-okuma yardımcı metodları (`Str()`, `Bool()` vb.) kullanır:

```razor
<div class="trace-detail">
    <h3>@Trace.TraceId</h3>

    <section>
        <h4>Kullanıcı Sorusu</h4>
        <blockquote>@Trace.UserQuery</blockquote>
    </section>

    <section>
        <h4>Reasoning</h4>
        @RenderReasoning(Trace.Reasoning)   <!-- JsonElement'i manuel gezen helper -->
    </section>

    <section>
        <h4>Planning</h4>
        @RenderPlanning(Trace.Planning)     <!-- JsonElement'i manuel gezen helper -->
    </section>

    <section>
        <h4>Agent Timeline</h4>
        @foreach (var visit in Trace.AgentVisits)
        {
            <div class="visit">
                <span class="name">@visit.AgentName</span>
                <span class="duration">@visit.DurationMs ms</span>
            </div>
        }
    </section>

    <section>
        <h4>Tool Calls</h4>
        @foreach (var tc in Trace.ToolCalls)
        {
            <div class="tool-call @(tc.Success ? "success" : "failed")">
                <code>@tc.ToolName</code>
                <small>@tc.AgentName · @FormatTime(tc.InvokedAt)</small>
                <pre>@tc.ParametersSummary</pre>
                <pre>@tc.ResultSummary</pre>
            </div>
        }
    </section>

    <section>
        <h4>Sonuç</h4>
        <p><strong>Durum:</strong> @Trace.TerminationReason</p>
        <p><strong>Yanıt:</strong> @Trace.FinalResponse</p>
        <small>@Trace.DurationMs ms</small>
    </section>

    @if (!string.IsNullOrEmpty(Trace.Error))
    {
        <section class="error">
            <h4>❌ Hata</h4>
            <pre>@Trace.Error</pre>
        </section>
    }
</div>
```

Gerçek `TraceDetailPanel.razor` yukarıdakinden daha kapsamlıdır — ayrıca **Chat History**, **Approval History**, **Escalation History**, **Specialist Reasonings** ve **Final Critique / Revision** (compound query'de ilk taslak vs. revize yanıt karşılaştırması) bölümlerini de render eder.

Bu detay sayfası **reasoning denetimi** için kritik — bot'un her kararının arkasındaki düşünce zinciri görülür.

---

## Replay — `/replay`

Trace'i adım adım yürüyerek izlenir. Animasyonlu walkthrough.

### State

```csharp
private string? _traceId;
private TraceDetail? _trace;
private int _currentStep = 0;
private int _totalSteps = 0;
private string _playbackState = "idle";   // idle | playing | paused | done
private double _speed = 1.0;
private Timer? _playbackTimer;
```

### Layout

```razor
<div class="replay-page">
    <header>
        <input @bind="_traceId" placeholder="Trace ID gir..." />
        <button @onclick="LoadTraceAsync">Yükle</button>
    </header>

    @if (_trace != null)
    {
        <div class="controls">
            <button @onclick="GoFirstStep">⏮</button>
            <button @onclick="GoPreviousStep">⏪</button>
            <button @onclick="TogglePlay">@(_playbackState == "playing" ? "⏸" : "▶")</button>
            <button @onclick="GoNextStep">⏩</button>
            <button @onclick="GoLastStep">⏭</button>

            <select @bind="_speed">
                <option value="0.5">0.5×</option>
                <option value="1">1×</option>
                <option value="2">2×</option>
                <option value="5">5×</option>
            </select>

            <span>@_currentStep / @_totalSteps</span>
        </div>

        <div class="timeline">
            <div class="progress" style="width: @(_currentStep * 100.0 / _totalSteps)%"></div>
        </div>

        <div class="step-display">
            @RenderCurrentStep()
        </div>
    }
</div>
```

### Query string ile trace ID

```csharp
protected override void OnInitialized()
{
    var uri = new Uri(Nav.Uri);
    var query = QueryHelpers.ParseQuery(uri.Query);
    if (query.TryGetValue("traceId", out var t))
    {
        _traceId = t;
        _ = LoadTraceAsync();   // Auto-load
    }
}
```

`/replay?traceId=abc-xxx` → otomatik yüklenir. Admin paneli "Source traces" linklerinde bu kullanılır.

### Playback timer

```csharp
private void TogglePlay()
{
    if (_playbackState == "playing")
    {
        _playbackTimer?.Dispose();
        _playbackState = "paused";
    }
    else
    {
        _playbackState = "playing";
        var interval = (int)(1000 / _speed);
        _playbackTimer = new Timer(_ => InvokeAsync(GoNextStep), null, interval, interval);
    }
}

private void GoNextStep()
{
    if (_currentStep >= _totalSteps)
    {
        _playbackTimer?.Dispose();
        _playbackState = "done";
        return;
    }
    _currentStep++;
    StateHasChanged();
}
```

`_speed` değişince timer interval da güncellenmeli (yeniden initialize).

---

## Sla — `/sla`

SLA Guardian'ın canlı durumu.

### Status cards

```razor
<div class="sla-status-grid">
    <div class="status-card approvals">
        <h3>📋 Approval SLA</h3>
        <div class="metric">Pending: <strong>@_status.Approvals.PendingCount</strong></div>
        <div class="metric">En Eski: <strong>@_status.Approvals.OldestSeconds sn</strong></div>
        <div class="threshold">
            <span>Warn: @_status.Approvals.WarnAfter sn</span>
            <span>Breach: @_status.Approvals.BreachAfter sn</span>
        </div>
        <div class="action">Breach'te: <strong>@_status.Approvals.OnBreach</strong></div>
        <div class="breach-count">Breach: @_status.Approvals.BreachCountRecent</div>
    </div>

    <div class="status-card escalations">
        <!-- Aynı pattern, escalation için -->
    </div>
</div>
```

### Events timeline

```razor
<section class="events">
    <h3>Son Event'ler</h3>
    @foreach (var evt in _events)
    {
        <div class="event @evt.Severity">
            <span class="time">@FormatTime(evt.Timestamp)</span>
            <span class="kind">@evt.Kind</span>
            <span class="severity">@evt.Severity</span>
            <code>@evt.TargetId</code>
            <span class="age">@evt.AgeSeconds sn</span>
            @if (!string.IsNullOrEmpty(evt.Action))
            {
                <span class="action">→ @evt.Action</span>
            }
            <small>@evt.Note</small>
        </div>
    }
</section>
```

### Auto-refresh — SSE veya polling?

```csharp
protected override async Task OnInitializedAsync()
{
    await RefreshAsync();
    _timer = new Timer(async _ => await InvokeAsync(RefreshAsync), null, 5000, 5000);
}

private async Task RefreshAsync()
{
    _status = await Sla.GetStatusAsync();
    _events = await Sla.GetEventsAsync(count: 50);
    StateHasChanged();
}
```

5 saniye polling. SSE alternatifi var ama bu sayfa için overkill (event frekansı düşük).

`sla-bridge.js` SSE versiyonunu sağlar ama default kullanılmıyor — polling tercih edildi (daha basit).

---

## Trace DTO'ları

`TraceSession` **`TracesApiService.cs`** içinde tanımlıdır (ayrı bir `TraceDetailModels.cs`'de değil), `TraceDetail`/`TraceAgentVisit`/`TraceToolCall` ise **`Models/TraceDetailModels.cs`**'de:

```csharp
// Services/TracesApiService.cs
public sealed record TraceSession(
    string SessionId,
    string? Title,
    int TraceCount,
    int MessageCount,
    DateTimeOffset LastTraceAt
);

// Models/TraceDetailModels.cs
public sealed class TraceDetail
{
    public string? TraceId { get; init; }
    public string? SessionId { get; init; }
    public string? UserQuery { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? TerminationReason { get; init; }
    public int? DurationMs { get; init; }
    public int? IterationCount { get; init; }
    public string? Error { get; init; }
    public string? FinalResponse { get; init; }
    public JsonElement? Reasoning { get; init; }         // ham JSON, tipli DTO yok
    public JsonElement? Planning { get; init; }           // ham JSON, tipli DTO yok
    public List<TraceAgentVisit> AgentVisits { get; init; } = [];
    public List<JsonElement> SpecialistReasonings { get; init; } = [];
    public List<TraceToolCall> ToolCalls { get; init; } = [];
    public bool WasRevised { get; init; }
    public string? FirstDraftResponse { get; init; }
    public JsonElement? FinalCritique { get; init; }
}

public sealed record TraceAgentVisit(string? AgentName, DateTimeOffset StartedAt, int? DurationMs, string? Output);
public sealed record TraceToolCall(string? ToolName, string? AgentName, DateTimeOffset? InvokedAt, bool Success, string? ParametersSummary, string? ResultSummary);
```

`EstimatedTokens`, `ReasoningSummary`, `PlanningSummary`, `AgentVisit` (tekil, tipli), `ToolInvocation` gibi tipler **kodda yoktur** — `Reasoning`/`Planning` API'den gelen JSON'u aynen taşıyan `JsonElement?`'tir, ayrı bir tipe deserialize edilmez.

---

## Bağlantılar

- [Api Endpoints-Observability](../api/Endpoints-Observability.md)
- [Domain Model-Trace](../domain/Model-Trace.md)
- [Services.md](Services.md) — TracesApiService, SlaApiService
- [Pages-Admin.md](Pages-Admin.md) — Improvements tab `/replay?traceId=` link kullanımı
