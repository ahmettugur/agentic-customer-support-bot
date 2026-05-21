// Application/Services/RealtimeBridgeService.cs
// IRealtimeBridge driving port'unun Application katmanı implementasyonu.
// Köprü modu orkestrasyonu burada; OpenAI Realtime transport IOpenAiRealtimeClient'ta.
//
// Akış:
//   RealtimeEndpoints (driving adapter) → IRealtimeBridge.RunAsync
//     → IOpenAiRealtimeClient (driven port, Adapters.AI)
//     → transkript hazır olunca IReasoningPort + IAgentTeamPort (agent pipeline)
//     → yanıt metni IOpenAiRealtimeClient.SpeakTextAsync ile seslendirmeye gönderilir

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Driven;
using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Application.Ports.Driven.Persistence;
using CustomerSupportBot.Application.Ports.Driving;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services;

/// <summary>
/// Köprü modu realtime oturumu — browser WS ↔ agent pipeline orkestrasyonu.
/// Her WebSocket bağlantısı için ayrı bir Scoped instance oluşturulur.
/// </summary>
public sealed class RealtimeBridgeService : IRealtimeBridge
{
    private readonly IOpenAiRealtimeClient _client;
    private readonly IAgentTeamPort _team;
    private readonly ISessionManager _sessionManager;
    private readonly IReasoningPort _reasoningService;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly IChatBridge _chatBridge;
    private readonly IInputGuard _inputGuard;
    private readonly ILogger<RealtimeBridgeService> _logger;

    // Half-duplex gating: asistan konuşurken browser audio iletilmez.
    // Volatile: event pump yazar, browser pump okur — lock gerekmez.
    private volatile bool _assistantSpeaking;

    public RealtimeBridgeService(
        IOpenAiRealtimeClient client,
        IAgentTeamPort team,
        ISessionManager sessionManager,
        IReasoningPort reasoningService,
        IApprovalContextAccessor approvalContext,
        IChatBridge chatBridge,
        IInputGuard inputGuard,
        ILogger<RealtimeBridgeService> logger)
    {
        _client = client;
        _team = team;
        _sessionManager = sessionManager;
        _reasoningService = reasoningService;
        _approvalContext = approvalContext;
        _chatBridge = chatBridge;
        _inputGuard = inputGuard;
        _logger = logger;
    }

    public async Task RunAsync(WebSocket browserWs, string sessionId, CancellationToken ct)
    {
        if (!_client.IsEnabled)
        {
            await SendBrowserJsonAsync(browserWs, new { type = "error", message = "Realtime özelliği kapalı." }, ct);
            return;
        }

        if (!await _client.TryConnectAsync(ct))
        {
            await SendBrowserJsonAsync(browserWs, new { type = "error", message = "OpenAI Realtime bağlantısı kurulamadı." }, ct);
            return;
        }

        var session = _sessionManager.GetOrCreate(sessionId);
        await _client.ConfigureBridgeSessionAsync(ct);

        await SendBrowserJsonAsync(browserWs, new
        {
            type = "connected",
            sessionId = session.SessionId,
            model = _client.ModelName,
            voice = _client.Voice
        }, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var browserPump = PumpBrowserAsync(browserWs, linked.Token);
        var eventPump   = HandleEventsAsync(browserWs, session, linked.Token);

        try
        {
            await Task.WhenAny(browserPump, eventPump);
            linked.Cancel();
            await Task.WhenAll(browserPump, eventPump).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeBridge: pump hatası session={Sid}", session.SessionId);
        }
    }

    // ─── Browser → OpenAI ───

    private async Task PumpBrowserAsync(WebSocket browserWs, CancellationToken ct)
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
                    _logger.LogInformation("RealtimeBridge: browser WS kapatıldı");
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
                await _client.SendAudioChunkAsync(payload, ct);
            }
            else if (result.MessageType == WebSocketMessageType.Text)
            {
                await HandleBrowserControlAsync(Encoding.UTF8.GetString(payload), ct);
            }
        }
    }

    private async Task HandleBrowserControlAsync(string json, CancellationToken ct)
    {
        string? type = null;
        try { type = JsonNode.Parse(json)?["type"]?.GetValue<string>(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RealtimeBridge: browser control mesajı parse edilemedi");
            return;
        }

        try
        {
            switch (type)
            {
                case "interrupt": await _client.SendInterruptAsync(ct); break;
                case "stop":      await _client.CloseAsync("client_stop", ct); break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RealtimeBridge: browser control işlenirken hata type={Type}", type);
        }
    }

    // ─── OpenAI → Browser + agent invoke ───

    private async Task HandleEventsAsync(WebSocket browserWs, AgentSession session, CancellationToken ct)
    {
        await foreach (var evt in _client.ReceiveEventsAsync(ct))
        {
            switch (evt.EventType)
            {
                case RealtimeServerEventType.ResponseCreated:
                    _assistantSpeaking = true;
                    break;

                case RealtimeServerEventType.SpeechStarted:
                    await SendBrowserJsonAsync(browserWs, new { type = "speech_started" }, ct);
                    break;

                case RealtimeServerEventType.SpeechStopped:
                    await SendBrowserJsonAsync(browserWs, new { type = "speech_stopped" }, ct);
                    break;

                case RealtimeServerEventType.InputTranscriptCompleted:
                {
                    var transcript = evt.Transcript;
                    if (string.IsNullOrWhiteSpace(transcript)) break;
                    await SendBrowserJsonAsync(browserWs, new { type = "user_transcript", text = transcript }, ct);
                    // Fire-and-forget: agent pipeline sürerken yeni ses alınmaya devam edilir.
                    _ = HandleUserTranscriptAsync(browserWs, session, transcript, ct);
                    break;
                }

                case RealtimeServerEventType.AudioDelta:
                    _assistantSpeaking = true;
                    if (evt.AudioDelta is { Length: > 0 })
                        await browserWs.SendAsync(evt.AudioDelta, WebSocketMessageType.Binary, true, ct);
                    break;

                case RealtimeServerEventType.AssistantTextDelta:
                    if (!string.IsNullOrEmpty(evt.TextDelta))
                        await SendBrowserJsonAsync(browserWs, new { type = "assistant_text_delta", text = evt.TextDelta }, ct);
                    break;

                case RealtimeServerEventType.ResponseDone:
                case RealtimeServerEventType.ResponseCancelled:
                    _assistantSpeaking = false;
                    await SendBrowserJsonAsync(browserWs, new { type = "response_done" }, ct);
                    break;

                case RealtimeServerEventType.Error:
                    _logger.LogWarning("RealtimeBridge: OpenAI error {Msg}", evt.ErrorMessage);
                    await SendBrowserJsonAsync(browserWs, new { type = "error", message = evt.ErrorMessage }, ct);
                    break;

                case RealtimeServerEventType.ConnectionClosed:
                    return;
            }
        }
    }

    private async Task HandleUserTranscriptAsync(
        WebSocket browserWs,
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
                await _client.SpeakTextAsync(
                    guard.RejectionReason ?? "Üzgünüm, bu mesajı işleyemiyorum.",
                    "Yukarıdaki metni Türkçe olarak doğal bir tonla harfiyen oku.", ct);
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
            await _client.SpeakTextAsync(
                responseText ?? "",
                "Yukarıda sana verilen son asistan metnini Türkçe olarak doğal, samimi bir tonla " +
                "harfiyen oku. Hiçbir kelime ekleme veya çıkarma.", ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeBridge: transcript handler hatası session={Sid}", sessionId);
            await SendBrowserJsonAsync(browserWs,
                new { type = "error", message = "İşlem sırasında hata oluştu." }, ct);
        }
    }

    private static Task ForwardStreamEventAsync(WebSocket browserWs, StreamEvent evt, CancellationToken ct)
        => SendBrowserJsonAsync(browserWs, new { type = evt.Type, data = evt.Data }, ct);

    private static string? TryExtractText(object data)
    {
        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (doc.RootElement.TryGetProperty("text", out var prop)) return prop.GetString();
        }
        catch { /* ignore */ }
        return null;
    }

    private static async Task SendBrowserJsonAsync(WebSocket ws, object payload, CancellationToken ct)
    {
        if (ws.State != WebSocketState.Open) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BrowserJsonOpts);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static readonly JsonSerializerOptions BrowserJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
