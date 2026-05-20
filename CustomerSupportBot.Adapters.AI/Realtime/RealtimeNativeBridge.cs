// Adapters.AI/Realtime/RealtimeNativeBridge.cs
//
// "Hızlı Sesli" mod — gpt-realtime-2 modelinin kendi çok-kipli yetenekleri kullanılır.
// Model sesi kendisi anlar, kararı kendisi verir, gerekirse function calling ile
// **sadece okuma-only** tool'ları çağırır, cevabı kendisi sesli okur.
//
// Köprü modundan (RealtimeBridge) farkı:
//   • create_response=true (model konuşur)
//   • tools listesi var → model function call yapabilir
//   • Reasoning + 7-agent pipeline ÇALIŞMAZ
//   • Sipariş oluşturma / şikayet kaydı tool'ları **bilinçli olarak yok** — bunlar
//     HITL gerektirdiği için sadece text chat veya köprü-sesli modunda yapılabilir.
//     Sistem prompt'u kullanıcıyı yazılı sohbete yönlendirir.
//
// Trade-off:
//   ✓ ~3-5× daha düşük latency, ~70% daha düşük token maliyeti
//   ✗ HITL/audit/reasoning trace yok — bu yüzden tool subset kısıtlı.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.AI.Realtime;

/// <summary>
/// Tek bir browser realtime-native bağlantısı için scoped servis.
/// Model kendisi konuşur ve okuma-only tool'ları doğrudan çağırır.
/// </summary>
public sealed class RealtimeNativeBridge : IRealtimeNativeBridge, IAsyncDisposable
{
    private const string OpenAiRealtimeUrl = "wss://api.openai.com/v1/realtime?model=";

    private readonly RealtimeOptions _options;
    private readonly string _apiKey;
    private readonly ISessionManager _sessionManager;
    private readonly RealtimeFunctionTools _toolDispatcher;
    private readonly IInputGuard _inputGuard;
    private readonly IChatBridge _chatBridge;
    private readonly ILogger<RealtimeNativeBridge> _logger;

    private ClientWebSocket? _openAiWs;

    // Half-duplex gating — bkz. RealtimeBridge açıklaması.
    private volatile bool _assistantSpeaking;

    // Model end_conversation tool'unu çağırdığında set edilir.
    private volatile bool _endRequested;
    private string _endReason = "user_farewell";

    // Inactivity tracking
    private long _lastUserActivityTicks = DateTime.UtcNow.Ticks;
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);

    public RealtimeNativeBridge(
        IOptions<AiOptions> aiOptions,
        ISessionManager sessionManager,
        RealtimeFunctionTools toolDispatcher,
        IInputGuard inputGuard,
        IChatBridge chatBridge,
        ILogger<RealtimeNativeBridge> logger)
    {
        _options = aiOptions.Value.Realtime;
        _apiKey = !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey!
            : aiOptions.Value.OpenAI.ApiKey ?? string.Empty;
        _sessionManager = sessionManager;
        _toolDispatcher = toolDispatcher;
        _inputGuard = inputGuard;
        _chatBridge = chatBridge;
        _logger = logger;
    }

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

        try
        {
            _openAiWs = new ClientWebSocket();
            _openAiWs.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

            var url = OpenAiRealtimeUrl + Uri.EscapeDataString(_options.Model);
            await _openAiWs.ConnectAsync(new Uri(url), ct);
            _logger.LogInformation("RealtimeNative: OpenAI bağlantısı açıldı session={Sid} model={Model}",
                actualSessionId, _options.Model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeNative: OpenAI bağlantısı açılamadı");
            await SendBrowserJsonAsync(browserWs,
                new { type = "error", message = "OpenAI Realtime bağlantısı kurulamadı: " + ex.Message }, ct);
            return;
        }

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
                            create_response = true,   // ← Köprü modundan farkı: model kendisi cevaplar
                            interrupt_response = true
                        }
                    },
                    output = new
                    {
                        format = new { type = "audio/pcm", rate = 24000 },
                        voice = _options.Voice
                    }
                },
                tools = _toolDispatcher.GetToolDefinitions(),
                tool_choice = "auto",
                instructions = BuildSystemInstructions()
            }
        }, ct);

        await SendBrowserJsonAsync(browserWs, new
        {
            type = "connected",
            mode = "native",
            sessionId = actualSessionId,
            model = _options.Model,
            voice = _options.Voice,
            tools = _toolDispatcher.GetToolNames()
        }, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var browserPump = PumpBrowserToOpenAiAsync(browserWs, _openAiWs, linked.Token);
        var openAiPump = PumpOpenAiToBrowserAsync(browserWs, _openAiWs, session, linked.Token);
        var inactivityWatcher = WatchInactivityAsync(browserWs, _openAiWs, linked.Token);

        try
        {
            await Task.WhenAny(browserPump, openAiPump, inactivityWatcher);
            linked.Cancel();
            await Task.WhenAll(browserPump, openAiPump, inactivityWatcher)
                .ContinueWith(_ => { }, TaskScheduler.Default);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeNative: pump hatası session={Sid}", actualSessionId);
        }
    }

    private async Task WatchInactivityAsync(WebSocket browserWs, ClientWebSocket openAiWs, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                if (_endRequested) return;

                var lastTicks = Interlocked.Read(ref _lastUserActivityTicks);
                var elapsed = DateTime.UtcNow - new DateTime(lastTicks, DateTimeKind.Utc);
                if (elapsed < InactivityTimeout) continue;

                _logger.LogInformation("RealtimeNative: inactivity timeout ({Sec}s) — görüşme sonlandırılıyor",
                    (int)elapsed.TotalSeconds);
                _endRequested = true;
                _endReason = "idle_timeout";

                try { await SendBrowserJsonAsync(browserWs, new { type = "conversation_ended", reason = _endReason }, ct); }
                catch { /* best effort */ }
                try
                {
                    if (openAiWs.State == WebSocketState.Open)
                        await openAiWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "idle_timeout", CancellationToken.None);
                }
                catch { /* best effort */ }
                try
                {
                    if (browserWs.State == WebSocketState.Open)
                        await browserWs.CloseAsync(WebSocketCloseStatus.NormalClosure, "idle_timeout", CancellationToken.None);
                }
                catch { /* best effort */ }
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    private static string BuildSystemInstructions() =>
        """
        Sen bir müşteri destek asistanısın. Türkçe konuş ve yanıtların KISA, doğal, samimi olsun (1-2 cümle, sesli okumaya uygun).

        YAPABİLDİKLERİN (function calling ile):
        - Ürün katalog sorgusu (fiyat/stok)
        - Sipariş durumu sorgulama (ORD-X numarasıyla)
        - Bir müşterinin son siparişi
        - Bir müşterinin tüm siparişlerinin listesi

        YAPAMADIKLARIN (bunları İSTEDİĞİNDE TOOL ÇAĞIRMA, kullanıcıyı yazılı sohbete yönlendir):
        - YENİ SİPARİŞ OLUŞTURMA
        - ŞİKAYET KAYDI OLUŞTURMA
        - İADE / İPTAL TALEBİ
        - ÖDEME / FATURA değişiklikleri
        - HESAP / ŞİFRE / KİŞİSEL BİLGİ değişiklikleri

        Bu istekler için kibarca şöyle de:
        "Bu işlemler güvenlik adımları gerektirdiği için yazılı sohbet üzerinden ilerletmeniz gerekiyor.
        Lütfen sohbet penceresine geçin, ben oradan da yardımcı olmaya devam edebilirim."

        KURALLAR:
        - Tool sonuçlarını YORUMLA, ham JSON OKUMA. Örneğin status:"shipped" → "kargoya verildi" de.
        - Tool başarısızsa kullanıcıya nazikçe açıkla, kendi uydurma cevap üretme.
        - Müşteri kimliği veya sipariş numarası eksikse iste; varsay-ma.
        - Asla başka dilde cevap verme.

        GÖRÜŞMEYİ SONLANDIRMA:
        - Kullanıcı açıkça vedalaştığında ("görüşürüz", "teşekkürler kapat", "hoşçakal",
          "başka soru yok", "yeterli" gibi) ÖNCE kısa bir veda cümlesi söyle
          (örn. "Tabii, iyi günler dilerim."), ARDINDAN end_conversation tool'unu çağır.
        - Kullanıcı açıkça vedalaşmadıkça end_conversation çağırma.
        """;

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
                if (result.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            var payload = ms.ToArray();
            if (payload.Length == 0) continue;

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                if (_assistantSpeaking) continue;

                Interlocked.Exchange(ref _lastUserActivityTicks, DateTime.UtcNow.Ticks);

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
        catch { return; }

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
            _logger.LogWarning(ex, "RealtimeNative: control mesajı işlenirken hata type={Type}", type);
        }
    }

    // ─── OpenAI → Browser + tool dispatch ───

    private async Task PumpOpenAiToBrowserAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        AgentSession session,
        CancellationToken ct)
    {
        var buffer = new byte[32 * 1024];
        var ms = new MemoryStream();
        var assistantTextBuilder = new StringBuilder();
        var pendingToolCalls = new List<(string CallId, string Name, string ArgsJson)>();
        var userTranscriptSent = false;
        var bufferedAssistantDeltas = new List<string>();

        while (!ct.IsCancellationRequested && openAiWs.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await openAiWs.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;

            var json = Encoding.UTF8.GetString(ms.ToArray());
            (userTranscriptSent, _) = await HandleOpenAiEventAsync(
                browserWs, openAiWs, session, json,
                assistantTextBuilder, pendingToolCalls,
                userTranscriptSent, bufferedAssistantDeltas, ct);
        }
    }

    private async Task<(bool UserTranscriptSent, bool _)> HandleOpenAiEventAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        AgentSession session,
        string json,
        StringBuilder assistantTextBuilder,
        List<(string CallId, string Name, string ArgsJson)> pendingToolCalls,
        bool userTranscriptSent,
        List<string> bufferedAssistantDeltas,
        CancellationToken ct)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch { return (userTranscriptSent, false); }
        if (node == null) return (userTranscriptSent, false);

        var type = node["type"]?.GetValue<string>() ?? "";

        switch (type)
        {
            case "session.created":
            case "session.updated":
                break;

            case "response.created":
                _assistantSpeaking = true;
                userTranscriptSent = false;
                bufferedAssistantDeltas.Clear();
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

                var guard = _inputGuard.Inspect(transcript);
                if (guard.Verdict == InputGuardVerdict.Reject)
                {
                    _logger.LogInformation("RealtimeNative: input guard reject session={Sid}", session.SessionId);
                    await SendOpenAiJsonAsync(openAiWs, new { type = "response.cancel" }, ct);
                    await SendBrowserJsonAsync(browserWs,
                        new { type = "user_transcript", text = transcript }, ct);
                    await SendBrowserJsonAsync(browserWs,
                        new { type = "error", message = guard.RejectionReason ?? "Mesaj işlenemedi." }, ct);
                    break;
                }

                Interlocked.Exchange(ref _lastUserActivityTicks, DateTime.UtcNow.Ticks);

                await SendBrowserJsonAsync(browserWs, new { type = "user_transcript", text = transcript }, ct);
                userTranscriptSent = true;
                if (bufferedAssistantDeltas.Count > 0)
                {
                    foreach (var delta in bufferedAssistantDeltas)
                    {
                        await SendBrowserJsonAsync(browserWs,
                            new { type = "assistant_text_delta", text = delta }, ct);
                    }
                    bufferedAssistantDeltas.Clear();
                }
                assistantTextBuilder.Clear();
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
                    assistantTextBuilder.Append(delta);
                    if (userTranscriptSent)
                    {
                        await SendBrowserJsonAsync(browserWs,
                            new { type = "assistant_text_delta", text = delta }, ct);
                    }
                    else
                    {
                        bufferedAssistantDeltas.Add(delta!);
                    }
                }
                break;
            }

            case "response.output_audio_transcript.done":
            {
                var fullText = node["transcript"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(fullText) && assistantTextBuilder.Length == 0)
                    assistantTextBuilder.Append(fullText);
                break;
            }

            case "response.function_call_arguments.done":
            {
                var callId = node["call_id"]?.GetValue<string>();
                var name   = node["name"]?.GetValue<string>();
                var args   = node["arguments"]?.GetValue<string>() ?? "{}";

                if (!string.IsNullOrWhiteSpace(callId) && !string.IsNullOrWhiteSpace(name))
                {
                    pendingToolCalls.Add((callId!, name!, args));

                    if (name == RealtimeFunctionTools.EndConversationToolName)
                    {
                        _endRequested = true;
                        try
                        {
                            var argNode = JsonNode.Parse(args) as JsonObject;
                            var reason = argNode?["reason"]?.GetValue<string>();
                            if (!string.IsNullOrWhiteSpace(reason)) _endReason = reason!;
                        }
                        catch { }
                    }

                    await SendBrowserJsonAsync(browserWs, new
                    {
                        type = "tool_call",
                        name,
                        arguments = args
                    }, ct);
                }
                else
                {
                    _logger.LogWarning("RealtimeNative: function_call event'inde call_id/name eksik");
                }
                break;
            }

            case "response.cancelled":
                _assistantSpeaking = false;
                pendingToolCalls.Clear();
                try { await SendOpenAiJsonAsync(openAiWs, new { type = "input_audio_buffer.clear" }, ct); }
                catch { /* best effort */ }
                assistantTextBuilder.Clear();
                break;

            case "response.done":
            {
                if (pendingToolCalls.Count > 0)
                {
                    await DispatchPendingToolCallsAsync(browserWs, openAiWs, pendingToolCalls, ct);
                    pendingToolCalls.Clear();
                    break;
                }

                _assistantSpeaking = false;
                try { await SendOpenAiJsonAsync(openAiWs, new { type = "input_audio_buffer.clear" }, ct); }
                catch { /* best effort */ }
                if (bufferedAssistantDeltas.Count > 0)
                {
                    foreach (var delta in bufferedAssistantDeltas)
                    {
                        await SendBrowserJsonAsync(browserWs,
                            new { type = "assistant_text_delta", text = delta }, ct);
                    }
                    bufferedAssistantDeltas.Clear();
                }
                var finalText = assistantTextBuilder.ToString().Trim();
                if (!string.IsNullOrEmpty(finalText))
                {
                    _chatBridge.RecordBotExchange(session.SessionId, "(sesli)", finalText);
                }
                await SendBrowserJsonAsync(browserWs,
                    new { type = "assistant_text", text = finalText }, ct);
                await SendBrowserJsonAsync(browserWs, new { type = "response_done" }, ct);
                assistantTextBuilder.Clear();

                if (_endRequested)
                {
                    await SendBrowserJsonAsync(browserWs,
                        new { type = "conversation_ended", reason = _endReason }, ct);
                    try
                    {
                        await openAiWs.CloseAsync(WebSocketCloseStatus.NormalClosure,
                            "end_conversation", CancellationToken.None);
                    }
                    catch { /* best effort */ }
                    try
                    {
                        if (browserWs.State == WebSocketState.Open)
                        {
                            await browserWs.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                "end_conversation", CancellationToken.None);
                        }
                    }
                    catch { /* best effort */ }
                }
                break;
            }

            case "error":
            {
                var msg = node["error"]?["message"]?.GetValue<string>() ?? "OpenAI realtime error";
                _logger.LogWarning("RealtimeNative: OpenAI error {Msg}", msg);
                await SendBrowserJsonAsync(browserWs, new { type = "error", message = msg }, ct);
                break;
            }
        }

        return (userTranscriptSent, false);
    }

    private async Task DispatchPendingToolCallsAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        List<(string CallId, string Name, string ArgsJson)> calls,
        CancellationToken ct)
    {
        var tasks = calls.Select(async c =>
        {
            string outputJson;
            try
            {
                outputJson = await _toolDispatcher.DispatchAsync(c.Name, c.ArgsJson, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RealtimeNative: tool dispatch hatası tool={Tool}", c.Name);
                outputJson = JsonSerializer.Serialize(new
                {
                    success = false,
                    error = new { code = "TOOL_DISPATCH_ERROR", message = ex.Message }
                });
            }
            return (c.CallId, c.Name, outputJson);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (callId, name, outputJson) in results)
        {
            await SendBrowserJsonAsync(browserWs, new
            {
                type = "tool_result",
                name,
                output = outputJson
            }, ct);

            await SendOpenAiJsonAsync(openAiWs, new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "function_call_output",
                    call_id = callId,
                    output = outputJson
                }
            }, ct);
        }

        if (_endRequested) return;

        await SendOpenAiJsonAsync(openAiWs, new { type = "response.create" }, ct);
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
