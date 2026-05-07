// Services/Realtime/RealtimeNativeBridge.cs
//
// "Hızlı Sesli" mod — gpt-realtime-1.5 modelinin kendi çok-kipli yetenekleri kullanılır.
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
using CustomerSupportBot.Models;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Services.Realtime;

/// <summary>
/// Tek bir browser realtime-native bağlantısı için scoped servis.
/// Model kendisi konuşur ve okuma-only tool'ları doğrudan çağırır.
/// </summary>
public sealed class RealtimeNativeBridge : IAsyncDisposable
{
    private const string OpenAiRealtimeUrl = "wss://api.openai.com/v1/realtime?model=";

    private readonly RealtimeOptions _options;
    private readonly string _apiKey;
    private readonly ISessionManager _sessionManager;
    private readonly RealtimeFunctionTools _toolDispatcher;
    private readonly InputGuard _inputGuard;
    private readonly IChatBridge _chatBridge;
    private readonly ILogger<RealtimeNativeBridge> _logger;

    private ClientWebSocket? _openAiWs;

    // Half-duplex gating — bkz. RealtimeBridge açıklaması. Bot konuşurken mikrofon
    // sesi OpenAI'a forward edilmez; aksi halde TTS hoparlörden mikrofona dolar ve
    // OpenAI VAD echo'yu yeni kullanıcı turu sanarak sahte yanıt üretir.
    private volatile bool _assistantSpeaking;

    public RealtimeNativeBridge(
        IOptions<AiOptions> aiOptions,
        ISessionManager sessionManager,
        RealtimeFunctionTools toolDispatcher,
        InputGuard inputGuard,
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

        // 2) Session config — model kendisi konuşur ve okuma-only tool'ları çağırabilir
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
                modalities = new[] { "audio", "text" },
                voice = _options.Voice,
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                input_audio_transcription = transcriptionConfig,
                turn_detection = new
                {
                    type = "server_vad",
                    threshold = _options.VadThreshold,
                    prefix_padding_ms = 300,
                    silence_duration_ms = _options.VadSilenceMs,
                    create_response = true   // ← Köprü modundan farkı: model kendisi cevaplar
                },
                tools = _toolDispatcher.GetToolDefinitions(),
                tool_choice = "auto",
                instructions = BuildSystemInstructions(),
                max_response_output_tokens = _options.MaxResponseTokens
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

        // 3) İki paralel pump
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var browserPump = PumpBrowserToOpenAiAsync(browserWs, _openAiWs, linked.Token);
        var openAiPump = PumpOpenAiToBrowserAsync(browserWs, _openAiWs, session, linked.Token);

        try
        {
            await Task.WhenAny(browserPump, openAiPump);
            linked.Cancel();
            await Task.WhenAll(browserPump, openAiPump).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeNative: pump hatası session={Sid}", actualSessionId);
        }
    }

    /// <summary>
    /// Modele rolünü, kapsamını ve hangi konularda kullanıcıyı yazılı sohbete
    /// yönlendireceğini bildiren sistem talimatı.
    /// </summary>
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
                // Half-duplex: bot konuşurken mikrofon sesini forward etme (echo loop kırıcı)
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
            await HandleOpenAiEventAsync(browserWs, openAiWs, session, json, assistantTextBuilder, ct);
        }
    }

    private async Task HandleOpenAiEventAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        AgentSession session,
        string json,
        StringBuilder assistantTextBuilder,
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
                // Model yanıt üretmeye başladı — ilk audio.delta gelmeden mic gating'i aç.
                // audio.delta ile arasındaki kısa pencerede de echo'yu önler.
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

                // NOT: Burada "_assistantSpeaking ise drop et" gibi bir echo-guard YOK.
                // OpenAI'da kullanıcı transcribe'ı asenkrondur ve sıklıkla model yanıtı
                // üretmeye başladıktan sonra tamamlanır; o noktada drop edersek gerçek
                // kullanıcı baloncuğu kaybolur. Echo'yu kaynağında kesen iki katman var:
                //  1) PumpBrowserToOpenAi: _assistantSpeaking iken binary audio drop
                //  2) Frontend: micTrack.enabled = false (bot konuşurken)
                // Yani buraya gelen transcript gerçek bir kullanıcı turudur.

                // Input guard — prompt injection vb.
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

                await SendBrowserJsonAsync(browserWs, new { type = "user_transcript", text = transcript }, ct);
                assistantTextBuilder.Clear();  // yeni tur başlıyor
                break;
            }

            case "response.audio.delta":
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

            case "response.audio_transcript.delta":
            {
                var delta = node["delta"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(delta))
                {
                    assistantTextBuilder.Append(delta);
                    await SendBrowserJsonAsync(browserWs,
                        new { type = "assistant_text_delta", text = delta }, ct);
                }
                break;
            }

            case "response.audio_transcript.done":
            {
                // Tek seferde tam transcript — bazen delta'lar atlanırsa burada toparlanır
                var fullText = node["transcript"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(fullText) && assistantTextBuilder.Length == 0)
                {
                    assistantTextBuilder.Append(fullText);
                }
                break;
            }

            case "response.function_call_arguments.done":
            {
                // Model bir tool çağırmaya karar verdi — argümanlar tamamlandı
                await HandleFunctionCallAsync(browserWs, openAiWs, node, ct);
                break;
            }

            case "response.cancelled":
                _assistantSpeaking = false;
                try { await SendOpenAiJsonAsync(openAiWs, new { type = "input_audio_buffer.clear" }, ct); }
                catch { /* best effort */ }
                assistantTextBuilder.Clear();
                break;

            case "response.done":
            {
                _assistantSpeaking = false;
                try { await SendOpenAiJsonAsync(openAiWs, new { type = "input_audio_buffer.clear" }, ct); }
                catch { /* best effort */ }
                var finalText = assistantTextBuilder.ToString().Trim();
                // Persist: kullanıcı transcript'i + asistan yanıtı (opsiyonel — idempotent olabilir)
                if (!string.IsNullOrEmpty(finalText))
                {
                    // En son user transcript'i bilmiyoruz burada — onun yerine session manager
                    // history'sinde son user mesajı zaten eklenmiş olmaz çünkü bu modda
                    // pipeline atlanıyor. Basit olarak sadece chat bridge'e yansıt.
                    _chatBridge.RecordBotExchange(session.SessionId, "(sesli)", finalText);
                }
                await SendBrowserJsonAsync(browserWs,
                    new { type = "assistant_text", text = finalText }, ct);
                await SendBrowserJsonAsync(browserWs, new { type = "response_done" }, ct);
                assistantTextBuilder.Clear();
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
    }

    /// <summary>
    /// <c>response.function_call_arguments.done</c> olayını işler:
    /// <list type="number">
    /// <item>argümanları parse eder</item>
    /// <item><see cref="RealtimeFunctionTools"/> üzerinden senkron dispatch eder</item>
    /// <item>sonucu <c>conversation.item.create</c> (function_call_output) ile gönderir</item>
    /// <item><c>response.create</c> ile modeli devam ettirir → modelin sesli yanıtı oluşur</item>
    /// </list>
    /// </summary>
    private async Task HandleFunctionCallAsync(
        WebSocket browserWs,
        ClientWebSocket openAiWs,
        JsonNode node,
        CancellationToken ct)
    {
        var callId = node["call_id"]?.GetValue<string>();
        var name = node["name"]?.GetValue<string>();
        var argumentsJson = node["arguments"]?.GetValue<string>() ?? "{}";

        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name))
        {
            _logger.LogWarning("RealtimeNative: function_call event'inde call_id/name eksik");
            return;
        }

        // Browser'a görsel ipucu (UI: "🛠 ürün sorgulanıyor...")
        await SendBrowserJsonAsync(browserWs, new
        {
            type = "tool_call",
            name,
            arguments = argumentsJson
        }, ct);

        string outputJson;
        try
        {
            outputJson = await _toolDispatcher.DispatchAsync(name!, argumentsJson, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeNative: tool dispatch hatası tool={Tool}", name);
            outputJson = JsonSerializer.Serialize(new
            {
                success = false,
                error = new { code = "TOOL_DISPATCH_ERROR", message = ex.Message }
            });
        }

        await SendBrowserJsonAsync(browserWs, new
        {
            type = "tool_result",
            name,
            output = outputJson
        }, ct);

        // Tool çıktısını conversation'a ekle
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

        // Modeli devam ettir → sesli yanıt üretsin
        await SendOpenAiJsonAsync(openAiWs, new
        {
            type = "response.create"
        }, ct);
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
