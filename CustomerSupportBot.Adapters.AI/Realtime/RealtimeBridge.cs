// Adapters.AI/Realtime/RealtimeBridge.cs
// OpenAI Realtime API ile browser arasında ses köprüsü.
//
// Akış:
//   Browser (PCM16 24kHz mic) ──WS binary──> Backend ──WS base64──> OpenAI Realtime
//   OpenAI ──transcript──> Backend ──invoke ChatStreamOrchestrator pipeline──>
//   final text ──response.create("speak verbatim")──> OpenAI ──audio delta──>
//   Backend ──WS binary──> Browser (PCM16 → AudioContext)
//
// Realtime modelin LLM yanıtı kapatılır (turn_detection.create_response=false);
// gerçek "düşünme" mevcut agent sisteminde olur. Realtime sadece STT + TTS köprüsü.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// Tek bir browser realtime bağlantısı için scoped servis.
/// <see cref="RunAsync"/> bağlantı yaşam süresi boyunca bloklayıcıdır.
/// </summary>
public sealed class RealtimeBridge : IRealtimeBridge, IAsyncDisposable
{
    private const string OpenAiRealtimeUrl = "wss://api.openai.com/v1/realtime?model=";

    private readonly RealtimeOptions _options;
    private readonly string _apiKey;
    private readonly IAgentTeamPort _team;
    private readonly ISessionManager _sessionManager;
    private readonly IReasoningPort _reasoningService;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly IChatBridge _chatBridge;
    private readonly IInputGuard _inputGuard;
    private readonly ILogger<RealtimeBridge> _logger;

    private ClientWebSocket? _openAiWs;

    // Half-duplex gating: bot konuşurken browser'dan gelen audio chunk'ları
    // OpenAI'a forward edilmez. Hoparlör → mikrofon echo loop'unu kırar.
    // Volatile, tek thread set / diğeri read — lock gerekmez.
    private volatile bool _assistantSpeaking;

    public RealtimeBridge(
        IOptions<AiOptions> aiOptions,
        IAgentTeamPort team,
        ISessionManager sessionManager,
        IReasoningPort reasoningService,
        IApprovalContextAccessor approvalContext,
        IChatBridge chatBridge,
        IInputGuard inputGuard,
        ILogger<RealtimeBridge> logger)
    {
        _options = aiOptions.Value.Realtime;
        // Realtime API anahtarı önce override, sonra OpenAI bölümü.
        _apiKey = !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey!
            : aiOptions.Value.OpenAI.ApiKey ?? string.Empty;
        _team = team;
        _sessionManager = sessionManager;
        _reasoningService = reasoningService;
        _approvalContext = approvalContext;
        _chatBridge = chatBridge;
        _inputGuard = inputGuard;
        _logger = logger;
    }

    /// <summary>
    /// Bridge'i çalıştırır — bağlantı kapanana veya iptal olana kadar bloke eder.
    /// </summary>
    public async Task RunAsync(WebSocket browserWs, string sessionId, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            await SendBrowserJsonAsync(browserWs, new { type = "error", message = "Realtime özelliği kapalı." }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            await SendBrowserJsonAsync(browserWs, new { type = "error", message = "Realtime API key yapılandırılmamış." }, ct);
            return;
        }

        var session = _sessionManager.GetOrCreate(sessionId);
        var actualSessionId = session.SessionId;

        // 1) OpenAI Realtime WS bağlantısı
        try
        {
            _openAiWs = new ClientWebSocket();
            _openAiWs.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

            var url = OpenAiRealtimeUrl + Uri.EscapeDataString(_options.Model);
            await _openAiWs.ConnectAsync(new Uri(url), ct);
            _logger.LogInformation("Realtime: OpenAI bağlantısı açıldı session={Sid} model={Model}",
                actualSessionId, _options.Model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Realtime: OpenAI bağlantısı açılamadı");
            await SendBrowserJsonAsync(browserWs,
                new { type = "error", message = "OpenAI Realtime bağlantısı kurulamadı: " + ex.Message }, ct);
            return;
        }

        // 2) Session config — model otomatik yanıt vermesin, sadece transcribe etsin.
        var transcriptionConfig = new Dictionary<string, object?>
        {
            ["model"] = _options.TranscriptionModel
        };
        if (!string.IsNullOrWhiteSpace(_options.TranscriptionLanguage))
            transcriptionConfig["language"] = _options.TranscriptionLanguage;
        if (!string.IsNullOrWhiteSpace(_options.TranscriptionPrompt))
            transcriptionConfig["prompt"] = _options.TranscriptionPrompt;

        await SendOpenAiJsonAsync(_openAiWs, new
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
                        format = new { type = "audio/pcm", rate = 24000 },
                        transcription = transcriptionConfig,
                        turn_detection = new
                        {
                            type = "semantic_vad",
                            eagerness = "medium",
                            create_response = false,  // ← Kritik: model otomatik yanıt vermesin
                            interrupt_response = true
                        }
                    },
                    output = new
                    {
                        format = new { type = "audio/pcm", rate = 24000 },
                        voice = _options.Voice
                    }
                },
                instructions = "Sen sadece bir ses-metin köprüsüsün. Kullanıcı konuşmasını kendin yorumlama. " +
                               "Yanıt verirken yalnızca sana verilen metni Türkçe olarak doğal bir tonla harfiyen oku."
            }
        }, ct);

        await SendBrowserJsonAsync(browserWs, new
        {
            type = "connected",
            sessionId = actualSessionId,
            model = _options.Model,
            voice = _options.Voice
        }, ct);

        // 3) İki paralel pump
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var browserPump = PumpBrowserToOpenAiAsync(browserWs, _openAiWs, linked.Token);
        var openAiPump = PumpOpenAiToBrowserAsync(browserWs, _openAiWs, session, linked.Token);

        try
        {
            var done = await Task.WhenAny(browserPump, openAiPump);
            linked.Cancel();
            await Task.WhenAll(browserPump, openAiPump).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Realtime: pump hatası session={Sid}", actualSessionId);
        }
    }

    // ─── Browser → OpenAI ───

    private async Task PumpBrowserToOpenAiAsync(WebSocket browserWs, ClientWebSocket openAiWs, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && browserWs.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await browserWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Realtime: browser kapatıldı");
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var payload = ms.ToArray();
            if (payload.Length == 0) continue;

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                if (_assistantSpeaking) continue;

                var b64 = Convert.ToBase64String(payload);
                await SendOpenAiJsonAsync(openAiWs, new
                {
                    type = "input_audio_buffer.append",
                    audio = b64
                }, ct);
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                await HandleBrowserControlAsync(openAiWs, Encoding.UTF8.GetString(payload), ct);
            }
        }
    }

    private async Task HandleBrowserControlAsync(ClientWebSocket openAiWs, string json, CancellationToken ct)
    {
        string? type = null;
        try
        {
            var node = JsonNode.Parse(json);
            type = node?["type"]?.GetValue<string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime: browser control mesajı parse edilemedi");
            return;
        }

        try
        {
            switch (type)
            {
                case "interrupt":
                    await SendOpenAiJsonAsync(openAiWs, new { type = "response.cancel" }, ct);
                    break;
                case "stop":
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    {
                        await openAiWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_stop", cts.Token);
                    }
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime: browser control mesajı işlenirken hata type={Type}", type);
        }
    }

    // ─── OpenAI → Browser + agent invoke ───

    private async Task PumpOpenAiToBrowserAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        AgentSession session,
        CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && openAiWs.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await openAiWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Realtime: OpenAI bağlantısı kapatıldı");
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;

            var json = Encoding.UTF8.GetString(ms.ToArray());
            await HandleOpenAiEventAsync(browserWs, openAiWs, session, json, ct);
        }
    }

    private async Task HandleOpenAiEventAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        AgentSession session,
        string json,
        CancellationToken ct)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch { return; }
        if (node == null) return;

        var type = node["type"]?.GetValue<string>() ?? "";

        switch (type)
        {
            case "session.created":
            case "session.updated":
                break;

            case "response.created":
                _assistantSpeaking = true;
                break;

            case "input_audio_buffer.speech_started":
                await SendBrowserJsonAsync(browserWs, new { type = "speech_started" }, ct);
                break;

            case "input_audio_buffer.speech_stopped":
                await SendBrowserJsonAsync(browserWs, new { type = "speech_stopped" }, ct);
                break;

            case "conversation.item.input_audio_transcription.completed":
            {
                var transcript = node["transcript"]?.GetValue<string>() ?? "";
                if (string.IsNullOrWhiteSpace(transcript)) break;

                await SendBrowserJsonAsync(browserWs, new { type = "user_transcript", text = transcript }, ct);
                _ = HandleUserTranscriptAsync(browserWs, openAiWs, session, transcript, ct);
                break;
            }

            case "response.output_audio.delta":
            {
                _assistantSpeaking = true;
                var b64 = node["delta"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(b64))
                {
                    var bytes = Convert.FromBase64String(b64);
                    await browserWs.SendAsync(bytes, WebSocketMessageType.Binary, true, ct);
                }
                break;
            }

            case "response.output_audio_transcript.delta":
            {
                var delta = node["delta"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(delta))
                {
                    await SendBrowserJsonAsync(browserWs,
                        new { type = "assistant_text_delta", text = delta }, ct);
                }
                break;
            }

            case "response.done":
            case "response.cancelled":
                _assistantSpeaking = false;
                try { await SendOpenAiJsonAsync(openAiWs, new { type = "input_audio_buffer.clear" }, ct); }
                catch { /* best effort */ }
                await SendBrowserJsonAsync(browserWs, new { type = "response_done" }, ct);
                break;

            case "error":
            {
                var msg = node["error"]?["message"]?.GetValue<string>() ?? "OpenAI realtime error";
                _logger.LogWarning("Realtime: OpenAI error {Msg}", msg);
                await SendBrowserJsonAsync(browserWs, new { type = "error", message = msg }, ct);
                break;
            }
        }
    }

    private async Task HandleUserTranscriptAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        AgentSession session,
        string transcript,
        CancellationToken ct)
    {
        var sessionId = session.SessionId;
        try
        {
            var guard = _inputGuard.Inspect(transcript);
            if (guard.Verdict == InputGuardVerdict.Reject)
            {
                await SendBrowserJsonAsync(browserWs,
                    new { type = "error", message = guard.RejectionReason ?? "Mesaj işlenemedi." }, ct);
                await SpeakAsync(openAiWs, guard.RejectionReason ?? "Üzgünüm, bu mesajı işleyemiyorum.", ct);
                return;
            }
            var safeQuery = guard.SanitizedInput;

            await SendBrowserJsonAsync(browserWs, new { type = "workflow_start" }, ct);

            var history = _sessionManager.GetHistory(sessionId);

            ReasoningResult? finalReasoning = null;
            await foreach (var evt in _reasoningService.ReasonStreamingAsync(safeQuery, session, history, ct))
            {
                await ForwardStreamEventAsync(browserWs, evt, ct);
                if (evt.Type == StreamEventTypes.ReasoningComplete && evt.Data is ReasoningResult rr)
                {
                    finalReasoning = rr;
                    if (!string.IsNullOrWhiteSpace(rr.Intent) && rr.Intent != WellKnown.Intents.Unknown)
                    {
                        session.State.CurrentIntent = rr.Intent;
                        _sessionManager.Update(session);
                    }
                }
            }

            using var approvalScope = _approvalContext.SetScope(sessionId, null, safeQuery);
            var responseBuilder = new StringBuilder();
            await foreach (var evt in _team.RunStreamingAsync(safeQuery, history, session, finalReasoning, ct))
            {
                await ForwardStreamEventAsync(browserWs, evt, ct);

                if (evt.Type == StreamEventTypes.ResponseDelta && evt.Data is not null)
                {
                    var text = TryExtractText(evt.Data);
                    if (!string.IsNullOrEmpty(text)) responseBuilder.Append(text);
                }
            }

            var responseText = responseBuilder.ToString().TrimEnd();

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                _sessionManager.AddExchange(sessionId, safeQuery, responseText);
                _chatBridge.RecordBotExchange(sessionId, safeQuery, responseText);
            }

            await SendBrowserJsonAsync(browserWs, new { type = "workflow_done" }, ct);
            await SpeakAsync(openAiWs, responseText ?? "", ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Realtime: transcript handler hatası session={Sid}", sessionId);
            await SendBrowserJsonAsync(browserWs,
                new { type = "error", message = "İşlem sırasında hata oluştu." }, ct);
        }
    }

    private static Task ForwardStreamEventAsync(WebSocket browserWs, StreamEvent evt, CancellationToken ct)
        => SendBrowserJsonAsync(browserWs, new { type = evt.Type, data = evt.Data }, ct);

    private async Task SpeakAsync(ClientWebSocket openAiWs, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        await SendOpenAiJsonAsync(openAiWs, new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role = "assistant",
                content = new[]
                {
                    new { type = "output_text", text }
                }
            }
        }, ct);

        await SendOpenAiJsonAsync(openAiWs, new
        {
            type = "response.create",
            response = new
            {
                output_modalities = new[] { "audio" },
                instructions = "Yukarıda sana verilen son asistan metnini Türkçe olarak doğal, " +
                               "samimi bir tonla harfiyen oku. Hiçbir kelime ekleme veya çıkarma."
            }
        }, ct);
    }

    private static string? TryExtractText(object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
                return textProp.GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    // ─── WS yardımcıları ───

    private static async Task SendOpenAiJsonAsync(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, OpenAiJsonOpts);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static async Task SendBrowserJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BrowserJsonOpts);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static readonly JsonSerializerOptions OpenAiJsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions BrowserJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_openAiWs?.State == WebSocketState.Open)
            {
                await _openAiWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "bridge_dispose", CancellationToken.None);
            }
        }
        catch { /* best effort */ }
        _openAiWs?.Dispose();
    }
}
