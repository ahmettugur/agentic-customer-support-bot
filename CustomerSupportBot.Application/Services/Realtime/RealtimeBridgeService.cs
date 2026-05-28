using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary>
/// Köprü modu realtime oturumu — browser kanalı ↔ agent pipeline orkestrasyonu.
/// Her bağlantı için ayrı bir Scoped instance oluşturulur.
/// </summary>
public sealed class RealtimeBridgeService : IRealtimeBridge
{
    private readonly IRealtimeVoiceTransport _client;
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
        IRealtimeVoiceTransport client,
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

    public async Task RunAsync(IBrowserChannel channel, string sessionId, CancellationToken ct)
    {
        if (!_client.IsEnabled)
        {
            await channel.SendJsonAsync(new { type = "error", message = "Realtime özelliği kapalı." }, ct);
            return;
        }

        if (!await _client.TryConnectAsync(ct))
        {
            await channel.SendJsonAsync(new { type = "error", message = "OpenAI Realtime bağlantısı kurulamadı." }, ct);
            return;
        }

        var session = _sessionManager.GetOrCreate(sessionId);
        await _client.ConfigureBridgeSessionAsync(ct);

        await channel.SendJsonAsync(new
        {
            type = "connected",
            sessionId = session.SessionId,
            model = _client.ModelName,
            voice = _client.Voice
        }, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var browserPump = PumpBrowserAsync(channel, linked.Token);
        var eventPump   = HandleEventsAsync(channel, session, linked.Token);

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

    private async Task PumpBrowserAsync(IBrowserChannel channel, CancellationToken ct)
    {
        await foreach (var msg in channel.ReceiveMessagesAsync(ct))
        {
            switch (msg.Kind)
            {
                case BrowserMessageKind.Closed:
                    _logger.LogInformation("RealtimeBridge: browser kanalı kapatıldı");
                    return;

                case BrowserMessageKind.Binary:
                    if (!_assistantSpeaking)
                        await _client.SendAudioChunkAsync(msg.Data!, ct);
                    break;

                case BrowserMessageKind.Text:
                    await HandleBrowserControlAsync(msg.AsText(), ct);
                    break;
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

    private async Task HandleEventsAsync(IBrowserChannel channel, AgentSession session, CancellationToken ct)
    {
        await foreach (var evt in _client.ReceiveEventsAsync(ct))
        {
            switch (evt.EventType)
            {
                case RealtimeServerEventType.ResponseCreated:
                    _assistantSpeaking = true;
                    break;

                case RealtimeServerEventType.SpeechStarted:
                    await channel.SendJsonAsync(new { type = "speech_started" }, ct);
                    break;

                case RealtimeServerEventType.SpeechStopped:
                    await channel.SendJsonAsync(new { type = "speech_stopped" }, ct);
                    break;

                case RealtimeServerEventType.InputTranscriptCompleted:
                {
                    var transcript = evt.Transcript;
                    if (string.IsNullOrWhiteSpace(transcript)) break;
                    await channel.SendJsonAsync(new { type = "user_transcript", text = transcript }, ct);
                    // Fire-and-forget: agent pipeline sürerken yeni ses alınmaya devam edilir.
                    _ = HandleUserTranscriptAsync(channel, session, transcript, ct);
                    break;
                }

                case RealtimeServerEventType.AudioDelta:
                    _assistantSpeaking = true;
                    if (evt.AudioDelta is { Length: > 0 })
                        await channel.SendBinaryAsync(evt.AudioDelta, ct);
                    break;

                case RealtimeServerEventType.AssistantTextDelta:
                    if (!string.IsNullOrEmpty(evt.TextDelta))
                        await channel.SendJsonAsync(new { type = "assistant_text_delta", text = evt.TextDelta }, ct);
                    break;

                case RealtimeServerEventType.ResponseDone:
                case RealtimeServerEventType.ResponseCancelled:
                    _assistantSpeaking = false;
                    await channel.SendJsonAsync(new { type = "response_done" }, ct);
                    break;

                case RealtimeServerEventType.Error:
                    _logger.LogWarning("RealtimeBridge: OpenAI error {Msg}", evt.ErrorMessage);
                    await channel.SendJsonAsync(new { type = "error", message = evt.ErrorMessage }, ct);
                    break;

                case RealtimeServerEventType.ConnectionClosed:
                    return;
            }
        }
    }

    private async Task HandleUserTranscriptAsync(
        IBrowserChannel channel,
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
                await channel.SendJsonAsync(
                    new { type = "error", message = guard.RejectionReason ?? "Mesaj işlenemedi." }, ct);
                await _client.SpeakTextAsync(
                    guard.RejectionReason ?? "Üzgünüm, bu mesajı işleyemiyorum.",
                    "Yukarıdaki metni Türkçe olarak doğal bir tonla harfiyen oku.", ct);
                return;
            }
            var safeQuery = guard.SanitizedInput;

            await channel.SendJsonAsync(new { type = "workflow_start" }, ct);

            var history = _sessionManager.GetHistory(sessionId);

            ReasoningResult? finalReasoning = null;
            await foreach (var evt in _reasoningService.ReasonStreamingAsync(safeQuery, session, history, ct))
            {
                await ForwardStreamEventAsync(channel, evt, ct);
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
                await ForwardStreamEventAsync(channel, evt, ct);
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

            await channel.SendJsonAsync(new { type = "workflow_done" }, ct);
            await _client.SpeakTextAsync(
                responseText ?? "",
                "Yukarıda sana verilen son asistan metnini Türkçe olarak doğal, samimi bir tonla " +
                "harfiyen oku. Hiçbir kelime ekleme veya çıkarma.", ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeBridge: transcript handler hatası session={Sid}", sessionId);
            await channel.SendJsonAsync(new { type = "error", message = "İşlem sırasında hata oluştu." }, ct);
        }
    }

    private static Task ForwardStreamEventAsync(IBrowserChannel channel, StreamEvent evt, CancellationToken ct)
        => channel.SendJsonAsync(new { type = evt.Type, data = evt.Data }, ct);

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
}
