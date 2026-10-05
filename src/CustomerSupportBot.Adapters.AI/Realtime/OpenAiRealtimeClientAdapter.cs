// Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.cs
// IRealtimeVoiceTransport secondary port'unun OpenAI Realtime WebSocket implementasyonu.
// ClientWebSocket, base64 ses encode, OpenAI event JSON şeması ve session.update protokolü
// bu sınıfta kapsüllenir — Application katmanı bunları bilmez.

using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// OpenAI Realtime WebSocket API için IRealtimeVoiceTransport implementasyonu.
/// Her WebSocket bağlantısı için Scoped bir instance oluşturulur.
/// </summary>
public sealed class OpenAiRealtimeClientAdapter : IRealtimeVoiceTransport
{
    private const string OpenAiRealtimeUrl = "wss://api.openai.com/v1/realtime?model=";

    /// <summary>
    /// Sesli asistanın sistem talimatı — <c>Prompts/services/realtime-voice.md</c>. Eskiden bu
    /// sınıfta gömülü bir sabitti; prompt klasörünün "tüm LLM prompt'ları burada" kuralının
    /// dışında kaldığı için yazılı ajanların kuralları güncellendiğinde sesli taraf geride
    /// kalıyordu (ör. kaydın kime ait olduğunu sızdırmama kuralı hiç yoktu).
    /// </summary>
    internal const string InstructionsPromptKey = "services/realtime-voice";

    private readonly RealtimeOptions _options;
    private readonly string _apiKey;
    private readonly RealtimeFunctionTools _functionTools;
    private readonly IPromptRepository _prompts;
    private readonly ILogger<OpenAiRealtimeClientAdapter> _logger;

    private ClientWebSocket? _ws;

    public OpenAiRealtimeClientAdapter(
        IOptions<AiOptions> aiOptions,
        RealtimeFunctionTools functionTools,
        IPromptRepository prompts,
        ILogger<OpenAiRealtimeClientAdapter> logger)
    {
        _options = aiOptions.Value.Realtime;
        _apiKey  = !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey!
            : aiOptions.Value.OpenAI.ApiKey ?? string.Empty;
        _functionTools = functionTools;
        _prompts       = prompts;
        _logger        = logger;
    }

    public bool IsEnabled  => _options.Enabled;
    public string ModelName => _options.Model;
    public string Voice     => _options.Voice;
    public IReadOnlyList<string> NativeToolNames => _functionTools.GetToolNames();
    public bool WaitsForInputGuard => _options.WaitForInputGuard;

    public async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        try
        {
            _ws = new ClientWebSocket();
            _ws.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            var url = OpenAiRealtimeUrl + Uri.EscapeDataString(_options.Model);
            await _ws.ConnectAsync(new Uri(url), ct);
            _logger.LogInformation("OpenAiRealtimeClient: bağlantı açıldı model={Model}", _options.Model);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAiRealtimeClient: bağlantı açılamadı");
            return false;
        }
    }

    public async Task ConfigureNativeSessionAsync(string? sessionContext, CancellationToken ct)
    {
        var instructions = BuildSessionInstructions(sessionContext);

        await SendJsonAsync(new
        {
            type = "session.update",
            session = new
            {
                type = "realtime",
                output_modalities = new[] { "audio" },
                audio = new
                {
                    input = new
                    {
                        format        = new { type = "audio/pcm", rate = 24000 },
                        transcription = BuildTranscriptionConfig(),
                        turn_detection = new
                        {
                            type              = "semantic_vad",
                            eagerness         = "medium",
                            // WaitForInputGuard: yanıt, transkript girdi korumasından geçince elle istenir.
                            create_response   = !_options.WaitForInputGuard,
                            interrupt_response = true
                        }
                    },
                    output = new { format = new { type = "audio/pcm", rate = 24000 }, voice = _options.Voice }
                },
                tools        = _functionTools.GetToolDefinitions(),
                tool_choice  = "auto",
                instructions = instructions
            }
        }, ct);
    }

    public async Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)
        => await SendJsonAsync(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(pcm16) }, ct);

    public async Task SendInterruptAsync(CancellationToken ct)
        => await SendJsonAsync(new { type = "response.cancel" }, ct);

    public async Task RequestResponseAsync(CancellationToken ct)
        => await SendJsonAsync(new { type = "response.create" }, ct);

    public async Task CloseAsync(string reason, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, cts.Token);
        }
        catch { /* best effort */ }
    }

    public async Task SendToolResultsAsync(
        IReadOnlyList<RealtimeToolResult> results,
        bool triggerNextResponse,
        CancellationToken ct)
    {
        foreach (var r in results)
        {
            await SendJsonAsync(new
            {
                type = "conversation.item.create",
                item = new
                {
                    type    = "function_call_output",
                    call_id = r.CallId,
                    output  = r.OutputJson
                }
            }, ct);
        }

        if (triggerNextResponse)
            await SendJsonAsync(new { type = "response.create" }, ct);
    }

    public async IAsyncEnumerable<RealtimeServerEvent> ReceiveEventsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            // yield olamaz try/catch içinde — okuma ayrı metoda çekildi.
            var (closed, text) = await TryReadFrameAsync(buffer, ms, ct);
            if (closed)
            {
                yield return new RealtimeServerEvent(RealtimeServerEventType.ConnectionClosed);
                yield break;
            }
            if (text == null) continue;

            var evt = ParseEvent(text);
            if (evt != null) yield return evt;
        }
    }

    private async Task<(bool closed, string? text)> TryReadFrameAsync(
        byte[] buffer, MemoryStream ms, CancellationToken ct)
    {
        try
        {
            WebSocketReceiveResult result;
            do
            {
                result = await _ws!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return (true, null);
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
                return (false, null);

            return (false, Encoding.UTF8.GetString(ms.ToArray()));
        }
        catch (OperationCanceledException) { return (true, null); }
        catch (WebSocketException)         { return (true, null); }
    }

    // ─── Private helpers ───

    internal static RealtimeServerEvent? ParseEvent(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch { return null; }
        if (node == null) return null;

        return node["type"]?.GetValue<string>() switch
        {
            "input_audio_buffer.speech_started" =>
                new RealtimeServerEvent(RealtimeServerEventType.SpeechStarted)
                { ItemId = node["item_id"]?.GetValue<string>() },

            "input_audio_buffer.speech_stopped" =>
                new RealtimeServerEvent(RealtimeServerEventType.SpeechStopped)
                { ItemId = node["item_id"]?.GetValue<string>() },

            "input_audio_buffer.committed" =>
                new RealtimeServerEvent(RealtimeServerEventType.InputAudioCommitted)
                { ItemId = node["item_id"]?.GetValue<string>() },

            "response.created" =>
                new RealtimeServerEvent(RealtimeServerEventType.ResponseCreated),

            "conversation.item.input_audio_transcription.completed" =>
                new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptCompleted)
                {
                    Transcript = node["transcript"]?.GetValue<string>(),
                    ItemId = node["item_id"]?.GetValue<string>()
                },

            // Canlı altyazı: konuşma sürerken (gpt-live-transcribe) ya da tur bitince (diğer
            // modeller) gelen parça. Yalnızca ekranda gösterilir, geçmişe yazılmaz.
            "conversation.item.input_audio_transcription.delta" =>
                new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptDelta)
                {
                    ItemId = node["item_id"]?.GetValue<string>(),
                    TextDelta = node["delta"]?.GetValue<string>()
                },

            "conversation.item.input_audio_transcription.failed" =>
                new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptFailed)
                {
                    ItemId = node["item_id"]?.GetValue<string>(),
                    ErrorMessage = node["error"]?["message"]?.GetValue<string>()
                },

            "response.output_audio.delta" =>
                new RealtimeServerEvent(RealtimeServerEventType.AudioDelta)
                { AudioDelta = TryDecodeBase64(node["delta"]?.GetValue<string>()) },

            "response.output_audio_transcript.delta" =>
                new RealtimeServerEvent(RealtimeServerEventType.AssistantTextDelta)
                { TextDelta = node["delta"]?.GetValue<string>() },

            "response.output_audio_transcript.done" =>
                new RealtimeServerEvent(RealtimeServerEventType.AssistantTextDone)
                { FullText = node["transcript"]?.GetValue<string>() },

            "response.function_call_arguments.done" =>
                new RealtimeServerEvent(RealtimeServerEventType.ToolCallReady)
                {
                    ToolCallId   = node["call_id"]?.GetValue<string>(),
                    ToolName     = node["name"]?.GetValue<string>(),
                    ToolArguments = node["arguments"]?.GetValue<string>() ?? "{}"
                },

            "response.done"      => new RealtimeServerEvent(RealtimeServerEventType.ResponseDone),
            "response.cancelled" => new RealtimeServerEvent(RealtimeServerEventType.ResponseCancelled),

            "error" =>
                new RealtimeServerEvent(RealtimeServerEventType.Error)
                { ErrorMessage = node["error"]?["message"]?.GetValue<string>() ?? "OpenAI realtime error" },

            "session.created" or "session.updated" => null,  // protokol mesajları — uygulama tarafı ilgilenmez
            _ => null
        };
    }

    private static byte[]? TryDecodeBase64(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try { return Convert.FromBase64String(b64); }
        catch { return null; }
    }

    private async Task SendJsonAsync(object payload, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    // Adaptör her WebSocket bağlantısında yeniden oluşturulur (scoped); ayar ise süreç
    // boyunca sabittir — yapılandırma notu her görüşmede değil, süreç başına bir kez loglanır.
    private static int s_transcriptionConfigLogged;

    /// <summary>
    /// Kullanıcı konuşmasının transkripsiyon ayarı — biçim model ailesine göre
    /// <see cref="RealtimeTranscriptionConfig"/>'te belirlenir.
    /// </summary>
    private Dictionary<string, object?> BuildTranscriptionConfig()
    {
        var (config, warnings) = RealtimeTranscriptionConfig.Build(_options);

        if (Interlocked.Exchange(ref s_transcriptionConfigLogged, 1) == 0)
        {
            _logger.LogInformation(
                "OpenAiRealtimeClient: transkripsiyon modeli={Model} aile={Family}",
                _options.TranscriptionModel, RealtimeTranscriptionConfig.Classify(_options.TranscriptionModel));
            foreach (var warning in warnings)
                _logger.LogWarning("OpenAiRealtimeClient: {Warning}", warning);
        }

        return config;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Oturumun sistem talimatı: prompt dosyası + oturuma özel bağlam (müşteri adı, bugünün
    /// tarihi) SONUNA eklenir — sesli modda mesaj listesi yok, tek enjeksiyon noktası
    /// <c>session.instructions</c>.
    /// </summary>
    internal string BuildSessionInstructions(string? sessionContext)
    {
        var instructions = _prompts.Get(InstructionsPromptKey).Trim();
        return string.IsNullOrWhiteSpace(sessionContext)
            ? instructions
            : $"{instructions}\n\nOTURUM BİLGİSİ:\n{sessionContext.Trim()}";
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws?.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None);
        }
        catch { /* best effort */ }
        _ws?.Dispose();
    }
}
