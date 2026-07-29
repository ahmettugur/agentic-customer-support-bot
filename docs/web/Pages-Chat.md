# Chat Page — Müşteri Arayüzü

**Dosya:** `Pages/Chat.razor`  
**Route:** `/`  
**Layout:** `EmptyLayout`  
**Auth:** Anonim

Müşteri ile bot arasındaki ana sohbet arayüzü.

---

## Özellikler

- 💬 Text chat (SSE streaming)
- 🎤 Voice (WebSocket + AudioWorklet)
- 🧠 Live reasoning panel (collapsible)
- 👤 Live agent takeover (human mode)
- ⭐ Rating widget (5 yıldız)
- 📝 Markdown rendering (Markdig)

---

## State değişkenleri

```csharp
private List<ChatMsg> _messages = new();
private string _sessionId = "";
private string _input = "";
private bool _humanMode = false;             // Agent canlı sohbete katıldı mı?
private bool _pendingHandoff = false;        // Escalation oluştu, agent bekleniyor
private bool _showRating = false;            // 2+ mesaj sonrası göster
private string? _agentName;                  // Şu an çalışan agent
private string _agentStatus = "";            // "running" / "done"
private ChatMsg? _currentBot;                // Streaming yanıt buffer'ı
private DotNetObjectReference<Chat>? _dotNetRef;
```

`ChatMsg` model:
```csharp
public sealed class ChatMsg
{
    public string Role { get; set; } = "user";   // user | assistant | agent | human | system
    public string Text { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}
```

---

## Lifecycle

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (!firstRender) return;

    // JS dosyalarını lazy load
    await JS.InvokeVoidAsync("loadScript", "/js/chat-bridge.js", "chat-bridge");
    await JS.InvokeVoidAsync("loadScript", "/js/realtime-client.js", "realtime-client");
    await JS.InvokeVoidAsync("loadScript", "/js/realtime-ui.js", "realtime-ui");

    // DotNet ref oluştur — JS'in C# metodlarını çağırması için
    _dotNetRef = DotNetObjectReference.Create(this);
    await JS.InvokeVoidAsync("__chatSetup", _dotNetRef, _apiBaseUrl);

    // İlk sessionId üret
    _sessionId = Guid.NewGuid().ToString("N");

    // Voice UI başlat
    await JS.InvokeVoidAsync("__initVoiceUi", _apiBaseUrl, _sessionId);

    // Persistent SSE — handoff event'leri için
    _ = StartPersistentEventsAsync(_sessionId);
}
```

`_dotNetRef` JS'te şu callback'leri çağırır:
- `OnStreamEvent(string type, string data)` — chat stream event
- `OnStreamComplete()` — yanıt tamamlandı
- `VoiceSendMessage(string text)` — voice transcript
- `VoiceTranscript(string text)` — kullanıcının söylediği
- `VoiceSetSession(string id)` — realtime-ui.js session id'yi bildirir; bu, `_startPersistentEvents(sid)` JS çağrısını tetikleyen gerçek mekanizmadır (bkz. [JsInterop.md](JsInterop.md))
- `OnPersistentEvent(string type, string data)` — handoff/human event

---

## Mesaj gönderme

```csharp
private async Task HandleSendAsync()
{
    if (string.IsNullOrWhiteSpace(_input)) return;
    if (_humanMode) { await SendHumanMessageAsync(); return; }   // Admin mode → ayrı path

    var text = _input.Trim();
    _input = "";

    // User mesajı listeye ekle
    _messages.Add(new ChatMsg { Role = "user", Text = text });

    // Streaming için boş bot mesajı oluştur
    _currentBot = new ChatMsg { Role = "assistant", Text = "" };
    _messages.Add(_currentBot);
    StateHasChanged();

    // JS'e devret — SSE fetch
    await JS.InvokeVoidAsync("__streamChat", _dotNetRef, _apiBaseUrl, text, _sessionId);
}
```

### JS akışı

```javascript
window.__streamChat = async (dotNetRef, apiBase, text, sessionId) => {
    const resp = await fetch(`${apiBase}/chat/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sessionId, message: text })
    });

    const reader = resp.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';

    while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        // SSE event parsing: "event: type\ndata: json\n\n"
        let idx;
        while ((idx = buffer.indexOf('\n\n')) !== -1) {
            const block = buffer.slice(0, idx);
            buffer = buffer.slice(idx + 2);

            const eventMatch = block.match(/event:\s*(.+)/);
            const dataMatch = block.match(/data:\s*(.+)/s);
            if (eventMatch && dataMatch) {
                await dotNetRef.invokeMethodAsync('OnStreamEvent', eventMatch[1], dataMatch[1]);
            }
        }
    }
    await dotNetRef.invokeMethodAsync('OnStreamComplete');
};
```

### `OnStreamEvent` — server event'leri işleme

```csharp
[JSInvokable]
public async Task OnStreamEvent(string type, string data)
{
    switch (type)
    {
        case "session":
            var sess = JsonSerializer.Deserialize<SessionEvent>(data);
            _sessionId = sess.SessionId;
            break;

        case "reasoning_start":
            _reasoningPanel = new ReasoningPanel { Status = "streaming" };
            break;
        case "reasoning_delta":
            _reasoningPanel.RawText += data;
            break;
        case "reasoning_complete":
            _reasoningPanel.Result = JsonSerializer.Deserialize<ReasoningResult>(data);
            _reasoningPanel.Status = "complete";
            break;

        case "agent":
            var agentEvt = JsonSerializer.Deserialize<AgentEvent>(data);
            _agentName = agentEvt.Name;
            _agentStatus = agentEvt.Status;
            break;

        case "response_delta":
            if (_currentBot != null)
                _currentBot.Text += data;
            break;
        case "response_complete":
            // Streaming bitti, _currentBot finalize edildi
            _currentBot = null;
            break;

        case "error":
            _messages.Add(new ChatMsg { Role = "system", Text = $"Hata: {data}" });
            break;
    }

    StateHasChanged();
}
```

---

## Reasoning paneli

Bot yanıtının üstünde collapsible bir panel — LLM'in düşünce sürecini gösterir.

```razor
@if (_reasoningPanel != null)
{
    <details class="reasoning-panel @_reasoningPanel.Status">
        <summary>🧠 Akıl yürütme</summary>
        @if (_reasoningPanel.Status == "complete" && _reasoningPanel.Result != null)
        {
            <div class="reasoning-body">
                <p>@_reasoningPanel.Result.Analysis</p>
                <ol>
                    @foreach (var step in _reasoningPanel.Result.Steps ?? new())
                    {
                        <li>
                            @step.Description
                            <span class="chip">@step.Action</span>
                            <span class="chip">@step.Grounding</span>
                            <span class="chip">conf: @step.Confidence.ToString("F2")</span>
                        </li>
                    }
                </ol>
            </div>
        }
        else
        {
            <pre class="streaming-text">@_reasoningPanel.RawText</pre>
        }
    </details>
}
```

Detay ([Domain Model-Reasoning](../domain/Model-Reasoning.md)): `analysis`, `steps`, `intent`, `requiredInfo`, `subTasks`.

---

## Agent chip'leri

Yanıt akarken hangi agent çalışıyor görüntüsü:

```razor
@if (!string.IsNullOrEmpty(_agentName))
{
    <div class="agent-chip @_agentStatus">
        @if (_agentStatus == "running") { <span>🧠</span> }
        else { <span>✓</span> }
        @FriendlyAgentName(_agentName)
        <small>@(_agentStatus == "running" ? "çalışıyor…" : "tamamlandı")</small>
    </div>
}

@functions {
    string FriendlyAgentName(string name) => name switch
    {
        "PlanningAgent" => "Planlama Ajanı",
        "OrderAgent" => "Sipariş Ajanı",
        "ComplaintAgent" => "Şikayet Ajanı",
        "ProductAgent" => "Ürün Ajanı",
        "HumanHandoffAgent" => "Yönlendirme Ajanı",
        "ResponseAgent" => "Yanıt Ajanı",
        _ => name
    };
}
```

---

## Voice integration

Voice UI **JavaScript tarafından** kontrol edilir (`realtime-ui.js`). Blazor sadece event callback'leri verir.

### Buton tetikleyiciler

```razor
<button id="voiceBtn">🎤 Sesli Asistan</button>          @* Bridge mode *@
<button id="voiceNativeBtn">⚡ Native Voice</button>      @* Native mode *@
<button id="voiceInterruptBtn" hidden>⏸ Durdur</button>
<div id="voiceStatus" hidden>
    <span id="voiceStatusText"></span>
</div>
```

JS bu buton'lara event listener ekler. Tıklayınca WebSocket bağlantısı açılır, AudioWorklet başlar (Detay: [JsInterop.md](JsInterop.md)).

### Voice callback'ler (JS → C#)

```csharp
[JSInvokable]
public async Task VoiceSendMessage(string text)
{
    // JS voice client tamamlanmış bir transcript verdi
    _input = text;
    await HandleSendAsync();
}

[JSInvokable]
public Task VoiceTranscript(string text)
{
    // Kullanıcı söylüyor → mesaj listesine ekle (incremental)
    if (_messages.LastOrDefault()?.Role == "user" && _messages[^1].Text.EndsWith("..."))
    {
        _messages[^1].Text = text;   // Update existing
    }
    else
    {
        _messages.Add(new ChatMsg { Role = "user", Text = text });
    }
    StateHasChanged();
    return Task.CompletedTask;
}
```

---

## Live takeover (admin mode)

`_pendingHandoff` ve `_humanMode` state'leri ChatEventOrchestrator'dan gelen SSE event'lerle güncellenir.

```csharp
[JSInvokable]
public Task OnPersistentEvent(string type, string data)
{
    switch (type)
    {
        case "handoff_pending":
            _pendingHandoff = true;
            _messages.Add(new ChatMsg { Role = "system", Text = "Temsilciye yönlendiriliyorsunuz..." });
            // 2 dakika timer başlat
            _ = StartHandoffTimerAsync();
            break;

        case "human_joined":
            var joined = JsonSerializer.Deserialize<HumanJoinedEvent>(data);
            _humanMode = true;
            _pendingHandoff = false;
            _messages.Add(new ChatMsg { Role = "system", Text = $"{joined.HumanAgent} sohbete katıldı" });
            break;

        case "human_left":
            _humanMode = false;
            _messages.Add(new ChatMsg { Role = "system", Text = "Temsilci ayrıldı, bot moduna dönülüyor" });
            break;

        case "human_message":
            var msg = JsonSerializer.Deserialize<HumanMessageEvent>(data);
            _messages.Add(new ChatMsg { Role = "human", Text = msg.Text });
            break;
    }
    StateHasChanged();
    return Task.CompletedTask;
}
```

### Input disabling

`_humanMode == true` iken:
- `HandleSendAsync` `SendHumanMessageAsync`'e dispatch eder (bot pipeline'ı atla)
- Direkt admin'e mesaj gider (`IChatBridge.PublishUserMessage`)

`_pendingHandoff == true` iken:
- Input disable, "Temsilci bağlanana kadar bekleyin..." placeholder
- 2 dakika timeout → "Temsilcimiz hâlâ müsait değil" mesaj

---

## Rating widget

```razor
@if (_showRating && !_humanMode)
{
    <div class="rating-widget">
        <p>Konuşma nasıldı?</p>
        @for (int i = 1; i <= 5; i++)
        {
            var star = i;
            <button @onclick="() => SelectStar(star)" class="@(_selectedStars >= star ? "selected" : "")">⭐</button>
        }
        <InputTextArea @bind-Value="_feedback" placeholder="(opsiyonel) Geri bildiriminiz..." />
        <button @onclick="SubmitRatingAsync">Gönder</button>
    </div>
}

@code {
    private async Task SubmitRatingAsync()
    {
        if (_selectedStars < 1) return;
        await ChatApi.SubmitRatingAsync(_sessionId, _selectedStars, _feedback);
        _messages.Add(new ChatMsg { Role = "system", Text = "⭐ Değerlendirmeniz alındı, teşekkürler!" });
        _showRating = false;
        StateHasChanged();
    }
}
```

`_showRating` 2+ user mesajı sonra true olur. Submit sonrası gizlenir.

---

## Markdown rendering

```csharp
private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .Build();

private MarkupString RenderMd(string text)
{
    var html = Markdown.ToHtml(text, _pipeline);
    return new MarkupString(html);
}
```

```razor
@if (msg.Role == "assistant")
{
    <div class="msg-bot">@RenderMd(msg.Text)</div>
}
else
{
    <div class="msg-@msg.Role">@msg.Text</div>
}
```

LLM yanıtları markdown formatlı dönebilir — listeler, kod blokları, bold/italic. Bot mesajları render edilir; kullanıcı mesajları plain text (XSS koruma).

⚠️ **XSS uyarısı:** `MarkupString` HTML escape **atlatır**. LLM çıktısının güvenilir olduğu varsayılır. Markdig'in default HTML escape mantığı yeterli — `<script>` tag'leri filtrelenir.

---

## Bağlantılar

- [JsInterop.md](JsInterop.md) — chat-bridge.js, realtime-* JS detayları
- [Api Endpoints-Chat](../api/Endpoints-Chat.md) — `/chat/stream`, `/chat/events/{sid}`
- [Domain Model-Reasoning](../domain/Model-Reasoning.md) — reasoning panel'in render ettiği yapı
- [Services.md](Services.md) — ChatApiService
