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
private TraceDetail? _currentTrace;
private bool _autoRefresh = false;
```

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
                Selected="@(sess.SessionId == _selectedSessionId)"
                OnClick="() => SelectSessionAsync(sess.SessionId)" />
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
                <span class="tokens">@trace.EstimatedTokens tk</span>
                @if (!string.IsNullOrEmpty(trace.Error))
                {
                    <span class="error-badge">❌</span>
                }
            </div>
        }
    </main>

    <!-- Sağ: detay paneli -->
    <aside class="detail-panel">
        @if (_currentTrace != null)
        {
            <TraceDetailPanel Trace="_currentTrace" />
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
_currentTrace = TraceDetail { ... full data ... }
```

`ShowAllTracesAsync` session filter'sız tüm trace'leri getirir (son N).

---

## TraceSessionItem component

```razor
<div class="session-item @(Selected ? "selected" : "")" @onclick="OnClick">
    <div class="title">@Session.Title</div>
    <div class="meta">
        <span>@Session.TraceCount trace</span>
        <span>@Session.MessageCount mesaj</span>
        <small>@FormatTime(Session.LastTraceAt)</small>
    </div>
</div>

@code {
    [Parameter] public TraceSession Session { get; set; } = null!;
    [Parameter] public bool Selected { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
}
```

Kart UI — sol sidebar'da liste item'ı. Reusable component (kod tekrarı azaltma).

---

## TraceDetailPanel component

Trace'in tüm detayını gösterir — reasoning ağacı, agent visit'leri, tool çağrıları.

```razor
<div class="trace-detail">
    <h3>@Trace.TraceId</h3>

    <section>
        <h4>Kullanıcı Sorusu</h4>
        <blockquote>@Trace.UserQuery</blockquote>
    </section>

    <section>
        <h4>Reasoning</h4>
        @if (Trace.Reasoning != null)
        {
            <p>@Trace.Reasoning.Analysis</p>
            <details>
                <summary>Adımlar (@Trace.Reasoning.Steps?.Count)</summary>
                <ol>
                    @foreach (var step in Trace.Reasoning.Steps ?? new())
                    {
                        <li>@step.Description <small>@step.Action · @step.Grounding</small></li>
                    }
                </ol>
            </details>
        }
    </section>

    <section>
        <h4>Planning</h4>
        @if (Trace.Planning != null)
        {
            <p>Intent: <strong>@Trace.Planning.DetectedIntent</strong></p>
            <p>Selected: <strong>@Trace.Planning.SelectedAgent</strong></p>
            <p>Rationale: <em>@Trace.Planning.Rationale</em></p>
        }
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
        <small>@Trace.DurationMs ms · @Trace.EstimatedTokens tokens</small>
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
        <div class="metric">En Eski: <strong>@_status.Approvals.OldestAgeSeconds sn</strong></div>
        <div class="threshold">
            <span>Warn: @_status.Approvals.WarnThresholdSeconds sn</span>
            <span>Breach: @_status.Approvals.BreachThresholdSeconds sn</span>
        </div>
        <div class="action">Breach'te: <strong>@_status.Approvals.OnBreachAction</strong></div>
        <div class="breach-count">Breach: @_status.Approvals.BreachCount</div>
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

## TraceDetailModels.cs

Web tarafının trace DTO'ları:

```csharp
public sealed class TraceSession
{
    public string SessionId { get; set; } = "";
    public string? Title { get; set; }
    public int TraceCount { get; set; }
    public int MessageCount { get; set; }
    public DateTime LastTraceAt { get; set; }
}

public sealed class TraceDetail
{
    public string TraceId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string UserQuery { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int EstimatedTokens { get; set; }
    public string? Error { get; set; }
    public string? TerminationReason { get; set; }
    public string? FinalResponse { get; set; }
    public ReasoningSummary? Reasoning { get; set; }
    public PlanningSummary? Planning { get; set; }
    public List<AgentVisit> AgentVisits { get; set; } = new();
    public List<ToolInvocation> ToolCalls { get; set; } = new();
}
```

Domain `ReasoningTrace`'in JSON-friendly hali. JSON deserialize için flat structure.

---

## Bağlantılar

- [Api Endpoints-Observability](../api/Endpoints-Observability.md)
- [Domain Model-Trace](../domain/Model-Trace.md)
- [Services.md](Services.md) — TracesApiService, SlaApiService
- [Pages-Admin.md](Pages-Admin.md) — Improvements tab `/replay?traceId=` link kullanımı
