// Adapters.AI/Realtime/OpenAiLiveClientAdapter.cs
// ILiveVoiceTransport secondary port'unun OpenAI GPT-Live WebSocket implementasyonu.
// session.start şeması, base64 ses ve iç içe response.event zarfı bu sınıfta kapsüllenir — Application katmanı bunları bilmez.

using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// GPT-Live için <see cref="ILiveVoiceTransport"/>. Bağlantı başına Scoped bir örnek oluşturulur. Tool'lar ses modeline değil,
/// <c>delegation.responses</c> altındaki arka plan modeline verilir; uygulamanın yaptığı iş <see cref="OpenAiRealtimeClientAdapter"/>
/// ile aynı tool'ları çalıştırmaktır.
/// </summary>
public sealed class OpenAiLiveClientAdapter : ILiveVoiceTransport
{
    /// <summary>Ses modelinin talimatı: konuşma üslubu ve ne zaman devredeceği.</summary>
    internal const string VoicePromptKey = "services/live-voice";

    /// <summary>Arka plan modelinin talimatı: iş kuralları ve tool sonuçlarını okuma.</summary>
    internal const string BackendPromptKey = "services/live-backend";

    /// <summary>
    /// Başlangıç geçmişi üst sınırı. Sağlayıcı 8.192 token kabul eder; token sayacı olmadığından Türkçe metinde güvenli kalan
    /// karakter sınırı kullanılır.
    /// </summary>
    internal const int MaxHistoryChars = 16_000;

    /// <summary>
    /// Bir pakette konuşma sayılacak en küçük tepe genlik. GPT-Live sessiz anlarda da paket gönderir; ölçülen sessiz paketlerin tepesi
    /// 0-92, konuşmanınki binlerce.
    /// </summary>
    private const int SpeechPeakThreshold = 200;

    private readonly LiveVoiceOptions _options;
    private readonly bool _enabled;
    private readonly string _apiKey;
    private readonly RealtimeFunctionTools _functionTools;
    private readonly IPromptRepository _prompts;
    private readonly ILogger<OpenAiLiveClientAdapter> _logger;

    // ClientWebSocket aynı anda tek gönderim kabul eder; mikrofon akışı, tool sonuçları ve talimatlar farklı akışlardan gelir.
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;

    public OpenAiLiveClientAdapter(
        IOptions<AiOptions> aiOptions,
        IOptions<LiveVoiceOptions> options,
        RealtimeFunctionTools functionTools,
        IPromptRepository prompts,
        ILogger<OpenAiLiveClientAdapter> logger)
    {
        _options = options.Value;
        var realtime = aiOptions.Value.Realtime;
        _enabled = realtime.Enabled;
        _apiKey = !string.IsNullOrWhiteSpace(realtime.ApiKey)
            ? realtime.ApiKey!
            : aiOptions.Value.OpenAI.ApiKey ?? string.Empty;
        _functionTools = functionTools;
        _prompts = prompts;
        _logger = logger;
    }

    public bool IsEnabled => _enabled;
    public string ModelName => _options.Model;
    public string Voice => _options.Voice;
    public IReadOnlyList<string> NativeToolNames => _functionTools.GetToolNames();

    public async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        try
        {
            _ws = new ClientWebSocket();
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            await _ws.ConnectAsync(new Uri(_options.Endpoint), ct);
            _logger.LogInformation("OpenAiLiveClient: bağlantı açıldı model={Model}", _options.Model);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAiLiveClient: bağlantı açılamadı");
            return false;
        }
    }

    public Task StartSessionAsync(LiveSessionSetup setup, CancellationToken ct) =>
        SendTextAsync(BuildSessionStartJson(setup), ct);

    public Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct) =>
        SendJsonAsync(new { type = "session.input_audio.append", audio = Convert.ToBase64String(pcm16) }, ct);

    public async Task SendToolResultsAsync(
        IReadOnlyList<RealtimeToolResult> results, bool continueBackend, CancellationToken ct)
    {
        foreach (var result in results)
        {
            await SendJsonAsync(new
            {
                type = "response.item.create",
                event_id = NewEventId(),
                item = new { type = "function_call_output", call_id = result.CallId, output = result.OutputJson }
            }, ct);
        }

        if (continueBackend)
            await SendJsonAsync(new { type = "response.create", event_id = NewEventId() }, ct);
    }

    public Task SendInstructionAsync(string text, CancellationToken ct) => SendTextAsync(BuildInstructionJson(text), ct);

    public Task RequestCloseAsync(CancellationToken ct) =>
        SendJsonAsync(new { type = "session.close", event_id = NewEventId() }, ct);

    public async IAsyncEnumerable<LiveServerEvent> ReceiveEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            var (closed, text) = await TryReadFrameAsync(buffer, ms, ct);
            if (closed)
            {
                yield return new LiveServerEvent(LiveServerEventType.ConnectionClosed);
                yield break;
            }
            if (text is null) continue;

            var evt = ParseEvent(text);
            if (evt is not null) yield return evt;
        }
    }

    private async Task<(bool closed, string? text)> TryReadFrameAsync(byte[] buffer, MemoryStream ms, CancellationToken ct)
    {
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) return (true, null);
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return result.MessageType == WebSocketMessageType.Text
                ? (false, Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length))
                : (false, null);
        }
        catch (OperationCanceledException) { return (true, null); }
        catch (WebSocketException) { return (true, null); }
    }

    // ─── Protokol ───

    internal static LiveServerEvent? ParseEvent(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return null; }
        if (node is null) return null;

        return Str(node, "type") switch
        {
            "session.started" => new LiveServerEvent(LiveServerEventType.SessionStarted),
            "session.output_audio.delta" => ParseAudio(Str(node, "delta")),

            "session.input_transcript.delta" =>
                new LiveServerEvent(LiveServerEventType.InputTranscriptDelta) { TextDelta = Str(node, "delta") },

            "session.output_transcript.delta" =>
                new LiveServerEvent(LiveServerEventType.OutputTranscriptDelta) { TextDelta = Str(node, "delta") },

            "session.delegation.created" => new LiveServerEvent(LiveServerEventType.DelegationCreated)
            {
                DelegationId = Str(node["delegation"], "id"),
                ResponseId = Str(node["delegation"], "response_id")
            },

            "response.event" => ParseBackendEvent(node["event"], Str(node, "delegation_id")),

            "session.usage.updated" => new LiveServerEvent(LiveServerEventType.UsageUpdated)
            {
                UsageSeconds = node["usage"]?["seconds"]?.GetValue<double>()
            },

            "session.closed" => new LiveServerEvent(LiveServerEventType.SessionClosed)
            {
                CloseReason = Str(node, "reason"),
                UsageSeconds = node["usage"]?["seconds"]?.GetValue<double>()
            },

            "error" => new LiveServerEvent(LiveServerEventType.Error)
            {
                ErrorCode = Str(node["error"], "code"),
                ErrorMessage = Str(node["error"], "message") ?? "OpenAI Live hatası"
            },

            _ => null
        };
    }

    /// <summary>
    /// Arka plan modelinin olayları. Yaşam döngüsü olaylarında <c>output</c> her zaman boştur: tool çağrıları yalnızca
    /// <c>response.output_item.done</c> ile gelir.
    /// </summary>
    private static LiveServerEvent? ParseBackendEvent(JsonNode? inner, string? delegationId)
    {
        switch (Str(inner, "type"))
        {
            case "response.output_item.done" when Str(inner?["item"], "type") == "function_call":
                var item = inner!["item"];
                return new LiveServerEvent(LiveServerEventType.ToolCallReady)
                {
                    DelegationId = delegationId,
                    ToolCallId = Str(item, "call_id"),
                    ToolName = Str(item, "name"),
                    ToolArguments = Str(item, "arguments") ?? "{}"
                };

            case "response.completed":
                return Lifecycle(LiveServerEventType.BackendResponseCompleted, inner!["response"], delegationId);

            case "response.failed" or "response.incomplete":
                var response = inner!["response"];
                return Lifecycle(LiveServerEventType.BackendResponseFailed, response, delegationId) with
                {
                    ErrorMessage = Str(response?["error"], "message") ?? Str(inner, "type")
                };

            default:
                return null;
        }
    }

    private static LiveServerEvent Lifecycle(LiveServerEventType type, JsonNode? response, string? delegationId) => new(type)
    {
        DelegationId = delegationId,
        ResponseId = Str(response, "id"),
        BackendModel = Str(response, "model"),
        InputTokens = response?["usage"]?["input_tokens"]?.GetValue<long>() ?? 0,
        OutputTokens = response?["usage"]?["output_tokens"]?.GetValue<long>() ?? 0
    };

    private static LiveServerEvent? ParseAudio(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        byte[] audio;
        try { audio = Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }

        return new LiveServerEvent(LiveServerEventType.AudioDelta) { AudioDelta = audio, HasSpeech = ContainsSpeech(audio) };
    }

    private static bool ContainsSpeech(byte[] pcm16)
    {
        foreach (var sample in MemoryMarshal.Cast<byte, short>(pcm16))
        {
            if (sample >= SpeechPeakThreshold || sample <= -SpeechPeakThreshold) return true;
        }
        return false;
    }

    private static string? Str(JsonNode? node, string key) =>
        node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal string BuildSessionStartJson(LiveSessionSetup setup)
    {
        var responses = new JsonObject
        {
            ["model"] = _options.BackendModel,
            ["instructions"] = BuildInstructions(BackendPromptKey, setup.SessionContext),
            ["tools"] = JsonSerializer.SerializeToNode(_functionTools.GetToolDefinitions(), JsonOpts),
            ["tool_choice"] = "auto",
            // Sonuçlar tek tek işlenir: yan etkili işlemler paralel çalıştırılmaz.
            ["parallel_tool_calls"] = false
        };
        if (!string.IsNullOrWhiteSpace(_options.BackendReasoningEffort))
            responses["reasoning"] = new JsonObject { ["effort"] = _options.BackendReasoningEffort };
        if (!string.IsNullOrWhiteSpace(_options.BackendServiceTier))
            responses["service_tier"] = _options.BackendServiceTier;

        return new JsonObject
        {
            ["type"] = "session.start",
            ["event_id"] = NewEventId(),
            ["session"] = new JsonObject
            {
                ["model"] = _options.Model,
                ["instructions"] = BuildInstructions(VoicePromptKey, setup.SessionContext),
                ["input"] = BuildHistory(setup.History),
                ["audio"] = new JsonObject
                {
                    ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
                    ["output"] = new JsonObject { ["voice"] = _options.Voice }
                },
                // Açık bırakılsa ses kaydı sağlayıcıda 30 gün tutulur ve silme ucu yoktur.
                ["store"] = false,
                ["delegation"] = new JsonObject { ["type"] = "responses", ["responses"] = responses }
            }
        }.ToJsonString();
    }

    /// <summary><c>delegation_id</c> zorunludur; genel talimatta açıkça <c>null</c> gönderilmelidir.</summary>
    internal static string BuildInstructionJson(string text) => new JsonObject
    {
        ["type"] = "session.instructions.append",
        ["event_id"] = NewEventId(),
        ["delegation_id"] = null,
        ["content"] = text
    }.ToJsonString();

    private string BuildInstructions(string promptKey, string? sessionContext)
    {
        var prompt = _prompts.Get(promptKey).Trim();
        return string.IsNullOrWhiteSpace(sessionContext)
            ? prompt
            : $"{prompt}\n\nOTURUM BİLGİSİ:\n{sessionContext.Trim()}";
    }

    private JsonArray BuildHistory(IReadOnlyList<ConversationMessage> history)
    {
        var selected = new List<ConversationMessage>();
        var chars = 0;
        foreach (var message in history.Reverse())
        {
            if (selected.Count >= Math.Max(0, _options.HistoryMessages)) break;
            if (!IsSeedable(message)) continue;
            chars += message.Text.Length;
            if (chars > MaxHistoryChars) break;
            selected.Add(message);
        }
        selected.Reverse();

        var input = new JsonArray();
        foreach (var message in selected)
        {
            var isUser = message.Role == ConversationRoles.User;
            input.Add(new JsonObject
            {
                ["type"] = "message",
                ["role"] = isUser ? "user" : "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = isUser ? "input_text" : "output_text",
                    ["text"] = message.Text
                })
            });
        }
        return input;
    }

    private static bool IsSeedable(ConversationMessage message) =>
        message.Role is ConversationRoles.User or ConversationRoles.Assistant
        && !string.IsNullOrWhiteSpace(message.Text)
        && message.Text != "(sesli)";

    private static string NewEventId() => $"evt_{Guid.NewGuid():N}";

    private Task SendJsonAsync(object payload, CancellationToken ct) =>
        SendTextAsync(JsonSerializer.Serialize(payload, JsonOpts), ct);

    private async Task SendTextAsync(string json, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_ws.State != WebSocketState.Open) return;
            await _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws?.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None);
        }
        catch { /* best effort */ }
        _ws?.Dispose();
        _sendLock.Dispose();
    }
}
