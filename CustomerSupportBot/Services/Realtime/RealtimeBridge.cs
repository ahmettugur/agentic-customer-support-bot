// Services/Realtime/RealtimeBridge.cs
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
using CustomerSupportBot.Agents;
using CustomerSupportBot.Models;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Services.Realtime;

/// <summary>
/// Tek bir browser realtime bağlantısı için scoped servis.
/// <see cref="RunAsync"/> bağlantı yaşam süresi boyunca bloklayıcıdır.
/// </summary>
public sealed class RealtimeBridge : IAsyncDisposable
{
    private const string OpenAiRealtimeUrl = "wss://api.openai.com/v1/realtime?model=";

    private readonly RealtimeOptions _options;
    private readonly string _apiKey;
    private readonly ICustomerSupportTeam _team;
    private readonly ISessionManager _sessionManager;
    private readonly ReasoningService _reasoningService;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly IChatBridge _chatBridge;
    private readonly InputGuard _inputGuard;
    private readonly ILogger<RealtimeBridge> _logger;

    private ClientWebSocket? _openAiWs;

    public RealtimeBridge(
        IOptions<AiOptions> aiOptions,
        ICustomerSupportTeam team,
        ISessionManager sessionManager,
        ReasoningService reasoningService,
        IApprovalContextAccessor approvalContext,
        IChatBridge chatBridge,
        InputGuard inputGuard,
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

        var session = _sessionManager.GetOrCreateSession(sessionId);
        var actualSessionId = session.SessionId;

        // 1) OpenAI Realtime WS bağlantısı
        try
        {
            _openAiWs = new ClientWebSocket();
            _openAiWs.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
            _openAiWs.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

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

        // 2) Session config — model otomatik yanıt vermesin, sadece transcribe etsin
        await SendOpenAiJsonAsync(_openAiWs, new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "audio", "text" },
                voice = _options.Voice,
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                input_audio_transcription = new { model = "whisper-1" },
                turn_detection = new
                {
                    type = "server_vad",
                    threshold = _options.VadThreshold,
                    prefix_padding_ms = 300,
                    silence_duration_ms = _options.VadSilenceMs,
                    create_response = false  // ← Kritik: model otomatik yanıt vermesin
                },
                instructions = "Sen sadece bir ses-metin köprüsüsün. Kullanıcı konuşmasını kendin yorumlama. " +
                               "Yanıt verirken yalnızca sana verilen metni Türkçe olarak doğal bir tonla harfiyen oku.",
                max_response_output_tokens = _options.MaxResponseTokens
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
            // İlki bitince diğerini de iptal et
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

    /// <summary>
    /// Browser'dan gelen ses (binary) ve kontrol mesajlarını (text) OpenAI'ye iletir.
    /// </summary>
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
                // PCM16 audio chunk → base64 → input_audio_buffer.append
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

    /// <summary>Browser'dan gelen JSON kontrol mesajlarını işler (interrupt, stop, vb.).</summary>
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
                    // Kullanıcı asistanı kesti — devam eden response'u iptal et.
                    // Aktif response yoksa OpenAI uyarı döner; client tarafında zaten
                    // sadece "speaking" state'inde gönderiliyor.
                    await SendOpenAiJsonAsync(openAiWs, new { type = "response.cancel" }, ct);
                    break;
                case "stop":
                    // Bağlantıyı kapat — browser zaten WS'i kapatmış olabileceği için
                    // RequestAborted token'ı tetiklenmiş olur. Close handshake'inde
                    // CancellationToken.None kullanıp 1sn timeout uygula.
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    {
                        await openAiWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_stop", cts.Token);
                    }
                    break;
                // diğer kontrol mesajları gerekirse buraya
            }
        }
        catch (OperationCanceledException) { /* shutdown sırasında normal */ }
        catch (WebSocketException) { /* zaten kapalı; sessizce yut */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Realtime: browser control mesajı işlenirken hata type={Type}", type);
        }
    }

    // ─── OpenAI → Browser + agent invoke ───

    /// <summary>
    /// OpenAI'den gelen event'leri parse eder, transcript geldiğinde agent workflow'unu tetikler,
    /// ses delta'larını browser'a iletir.
    /// </summary>
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
                // sessizce yut
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

            case "response.audio.delta":
            {
                // PCM16 audio chunk'ı browser'a binary olarak forward et
                var b64 = node["delta"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(b64))
                {
                    var bytes = Convert.FromBase64String(b64);
                    await browserWs.SendAsync(bytes, WebSocketMessageType.Binary, true, ct);
                }
                break;
            }

            case "response.audio_transcript.delta":
            {
                // Asistanın söylediği metnin delta'sı — browser bunu altyazı gibi gösterebilir
                var delta = node["delta"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(delta))
                {
                    await SendBrowserJsonAsync(browserWs,
                        new { type = "assistant_text_delta", text = delta }, ct);
                }
                break;
            }

            case "response.done":
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

    /// <summary>
    /// Kullanıcı transcript'i geldiğinde mevcut agent pipeline'ını tetikler ve
    /// final text'i Realtime'a TTS olarak gönderir.
    /// </summary>
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
            // Input guard
            var guard = _inputGuard.Inspect(transcript);
            if (guard.Verdict == InputGuardVerdict.Reject)
            {
                await SpeakAsync(openAiWs, guard.RejectionReason ?? "Üzgünüm, bu mesajı işleyemiyorum.", ct);
                return;
            }
            var safeQuery = guard.SanitizedInput;

            await SendBrowserJsonAsync(browserWs, new { type = "workflow_start" }, ct);

            // Mevcut pipeline: reasoning + workflow
            var history = _sessionManager.GetHistory(sessionId);
            var reasoning = await _reasoningService.ReasonAsync(safeQuery, session, history);

            using var approvalScope = _approvalContext.SetScope(sessionId, null, safeQuery);
            var responseText = await _team.RunAsync(safeQuery, history, session, reasoning);

            if (!string.IsNullOrWhiteSpace(reasoning?.Intent) && reasoning!.Intent != WellKnown.Intents.Unknown)
            {
                session.State.CurrentIntent = reasoning.Intent;
                _sessionManager.UpdateSession(session);
            }

            if (!string.IsNullOrWhiteSpace(responseText))
            {
                _sessionManager.AddExchange(sessionId, safeQuery, responseText);
                _chatBridge.RecordBotExchange(sessionId, safeQuery, responseText);
            }

            await SendBrowserJsonAsync(browserWs,
                new { type = "assistant_text", text = responseText }, ct);
            await SendBrowserJsonAsync(browserWs, new { type = "workflow_done" }, ct);

            await SpeakAsync(openAiWs, responseText ?? "", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Realtime: transcript handler hatası session={Sid}", sessionId);
            await SendBrowserJsonAsync(browserWs,
                new { type = "error", message = "İşlem sırasında hata oluştu." }, ct);
        }
    }

    /// <summary>
    /// Hazır metni Realtime API'ye TTS olarak söyletir.
    /// conversation.item.create + response.create ile assistant role mesajı enjekte eder.
    /// </summary>
    private async Task SpeakAsync(ClientWebSocket openAiWs, string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        // Önce assistant mesajını conversation'a ekle (sonradan model bunu hatırlasın)
        await SendOpenAiJsonAsync(openAiWs, new
        {
            type = "conversation.item.create",
            item = new
            {
                type = "message",
                role = "assistant",
                content = new[]
                {
                    new { type = "text", text }
                }
            }
        }, ct);

        // Sonra response.create ile sadece audio modalitesinde "yukarıdaki metni oku" talimatı
        await SendOpenAiJsonAsync(openAiWs, new
        {
            type = "response.create",
            response = new
            {
                modalities = new[] { "audio", "text" },
                instructions = "Yukarıda sana verilen son asistan metnini Türkçe olarak doğal, " +
                               "samimi bir tonla harfiyen oku. Hiçbir kelime ekleme veya çıkarma."
            }
        }, ct);
    }

    // ─── WS yardımcıları ───

    private static async Task SendOpenAiJsonAsync(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        // OpenAI snake_case bekliyor; anonymous type property adları zaten input_audio_format gibi
        // yazılmıştır — naming policy uygulanmadan birebir gönderilir.
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
