// Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.cs
// IRealtimeVoiceTransport secondary port'unun OpenAI Realtime WebSocket implementasyonu.
// ClientWebSocket, base64 ses encode, OpenAI event JSON şeması ve session.update protokolü
// bu sınıfta kapsüllenir — Application katmanı bunları bilmez.

using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Driven.AI;
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

    private readonly RealtimeOptions _options;
    private readonly string _apiKey;
    private readonly RealtimeFunctionTools _functionTools;
    private readonly ILogger<OpenAiRealtimeClientAdapter> _logger;

    private ClientWebSocket? _ws;

    public OpenAiRealtimeClientAdapter(
        IOptions<AiOptions> aiOptions,
        RealtimeFunctionTools functionTools,
        ILogger<OpenAiRealtimeClientAdapter> logger)
    {
        _options = aiOptions.Value.Realtime;
        _apiKey  = !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey!
            : aiOptions.Value.OpenAI.ApiKey ?? string.Empty;
        _functionTools = functionTools;
        _logger        = logger;
    }

    public bool IsEnabled  => _options.Enabled;
    public string ModelName => _options.Model;
    public string Voice     => _options.Voice;
    public IReadOnlyList<string> NativeToolNames => _functionTools.GetToolNames();

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

    public async Task ConfigureBridgeSessionAsync(CancellationToken ct)
    {
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
                        format       = new { type = "audio/pcm", rate = 24000 },
                        transcription = BuildTranscriptionConfig(),
                        turn_detection = new
                        {
                            type             = "semantic_vad",
                            eagerness        = "medium",
                            create_response  = false,  // model otomatik cevap vermesin
                            interrupt_response = true
                        }
                    },
                    output = new { format = new { type = "audio/pcm", rate = 24000 }, voice = _options.Voice }
                },
                instructions = "Sen sadece bir ses-metin köprüsüsün. Kullanıcı konuşmasını kendin yorumlama. " +
                               "Yanıt verirken yalnızca sana verilen metni Türkçe olarak doğal bir tonla harfiyen oku."
            }
        }, ct);
    }

    public async Task ConfigureNativeSessionAsync(CancellationToken ct)
    {
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
                            create_response   = true,  // model kendisi cevaplar
                            interrupt_response = true
                        }
                    },
                    output = new { format = new { type = "audio/pcm", rate = 24000 }, voice = _options.Voice }
                },
                tools        = _functionTools.GetToolDefinitions(),
                tool_choice  = "auto",
                instructions = NativeSystemInstructions
            }
        }, ct);
    }

    public async Task SendAudioChunkAsync(byte[] pcm16, CancellationToken ct)
        => await SendJsonAsync(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(pcm16) }, ct);

    public async Task SendInterruptAsync(CancellationToken ct)
        => await SendJsonAsync(new { type = "response.cancel" }, ct);

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

    public async Task SpeakTextAsync(string text, string speakInstructions, CancellationToken ct)
    {
        await SendJsonAsync(new
        {
            type = "conversation.item.create",
            item = new
            {
                type    = "message",
                role    = "assistant",
                content = new[] { new { type = "output_text", text } }
            }
        }, ct);

        await SendJsonAsync(new
        {
            type     = "response.create",
            response = new
            {
                output_modalities = new[] { "audio" },
                instructions      = speakInstructions
            }
        }, ct);
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

    private static RealtimeServerEvent? ParseEvent(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch { return null; }
        if (node == null) return null;

        return node["type"]?.GetValue<string>() switch
        {
            "input_audio_buffer.speech_started" =>
                new RealtimeServerEvent(RealtimeServerEventType.SpeechStarted),

            "input_audio_buffer.speech_stopped" =>
                new RealtimeServerEvent(RealtimeServerEventType.SpeechStopped),

            "response.created" =>
                new RealtimeServerEvent(RealtimeServerEventType.ResponseCreated),

            "conversation.item.input_audio_transcription.completed" =>
                new RealtimeServerEvent(RealtimeServerEventType.InputTranscriptCompleted)
                { Transcript = node["transcript"]?.GetValue<string>() },

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

    private Dictionary<string, object?> BuildTranscriptionConfig()
    {
        var cfg = new Dictionary<string, object?> { ["model"] = _options.TranscriptionModel };
        if (!string.IsNullOrWhiteSpace(_options.TranscriptionLanguage))
            cfg["language"] = _options.TranscriptionLanguage;
        if (!string.IsNullOrWhiteSpace(_options.TranscriptionPrompt))
            cfg["prompt"] = _options.TranscriptionPrompt;
        return cfg;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ─── Native mod sistem talimatları ───

    private const string NativeSystemInstructions = """
        Sen bir müşteri destek asistanısın. Türkçe konuş ve yanıtların KISA, doğal, samimi olsun (1-2 cümle, sesli okumaya uygun).

        YAPABİLDİKLERİN (function calling ile):
        - Ürün katalog sorgusu (fiyat/stok)
        - Sipariş durumu sorgulama (4+ haneli sipariş numarasıyla)
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
