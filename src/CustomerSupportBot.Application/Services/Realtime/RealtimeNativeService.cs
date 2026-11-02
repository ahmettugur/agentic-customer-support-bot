// Application/Services/RealtimeNativeService.cs
// IRealtimeNativeBridge driving port'unun Application katmanı implementasyonu.
// Model kendi karar verir ve yazılı sohbetin iş tool'larının tamamını çağırabilir; yan etkili
// olanlar (sipariş/iptal/iade/şikayet) yazılı sohbetle aynı HITL onay kapısından geçer.
// Tool dispatch iş mantığı (hangi araçlar sesli modda kullanılabilir) burada kapsüllenir.
// Tarayıcı kanalı IBrowserChannel'da, realtime voice transport IRealtimeVoiceTransport'ta gizlenir.

using System.Text;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary>
/// Sesli görüşme oturumu — model konuşur ve tool'ları doğrudan çağırır. Okuma tool'ları hemen
/// çalışır; yan etkili tool'lar (sipariş, iptal, iade, şikayet kaydı) işlemi yapmaz,
/// <see cref="SideEffectApprovalGate"/> ile onay kaydı oluşturur — yazılı sohbetle aynı insan onayı.
/// </summary>
public sealed class RealtimeNativeService : IRealtimeNativeBridge
{
    private const string EndConversationToolName = VoiceToolDispatcher.EndConversationToolName;

    /// <summary>Native sesli kanalın açtığı eskalasyonlarda ajan adı (session + ajan dedup anahtarı).</summary>
    internal const string VoiceAgentName = VoiceTurnRecorder.VoiceAgentName;
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);

    private readonly IRealtimeVoiceTransport _client;
    private readonly ISessionManager _sessionManager;
    private readonly VoiceToolDispatcher _dispatcher;
    private readonly VoiceTurnRecorder _recorder;
    private readonly IInputGuard _inputGuard;
    private readonly CustomerIdentityHintBuilder _identityHint;
    private readonly IChatBridge _chatBridge;
    private readonly IAppDistributedLock _sessionLock;
    private readonly ILogger<RealtimeNativeService> _logger;

    private volatile bool _assistantSpeaking;
    private volatile bool _endRequested;
    private string _endReason = "user_farewell";
    private long _lastUserActivityTicks = DateTime.UtcNow.Ticks;

    public RealtimeNativeService(
        IRealtimeVoiceTransport client,
        ISessionManager sessionManager,
        CustomerSupportToolsService tools,
        IInputGuard inputGuard,
        IChatBridge chatBridge,
        CustomerIdentityHintBuilder identityHint,
        IAppDistributedLock sessionLock,
        ILogger<RealtimeNativeService> logger,
        SessionStateService? sessionState = null,
        IEscalationSink? escalations = null,
        SideEffectApprovalGate? approvalGate = null)
    {
        _client = client;
        _sessionManager = sessionManager;
        _dispatcher = new VoiceToolDispatcher(tools, logger, escalations, approvalGate);
        _recorder = new VoiceTurnRecorder(chatBridge, sessionManager, logger, sessionState, escalations);
        _inputGuard = inputGuard;
        _identityHint = identityHint;
        _chatBridge = chatBridge;
        _sessionLock = sessionLock;
        _logger = logger;
    }

    public async Task RunAsync(
        IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)
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

        // Kimliği oturuma ATOMİK bağla: kilit + kalıcı depodan tazeleme + bağlama.
        // Bu satır olmadan session.State.AuthenticatedCustomerId boş kalıyordu ve sipariş
        // tool'ları customerId="" ile koşup sahiplik kontrolüne takılıyordu — kullanıcıya
        // "sipariş bulunamadı" olarak yansıyordu (bkz. DispatchTool).
        //
        // Yazılı sohbetle AYNI mekanizma ve aynı kilit anahtarı kullanılır; iki kanal aynı
        // oturuma eşzamanlı bağlanmaya çalıştığında sahiplik yarışı doğmaz.
        var session = await SessionIdentityBinder.BindAtomicallyAsync(
            sessionId, authenticatedCustomerId, _sessionManager, _sessionLock, ct);

        if (session is null)
        {
            _logger.LogWarning(
                "RealtimeNative: oturum başka bir müşteriye ait, bağlantı reddedildi session={Sid}", sessionId);
            await channel.SendJsonAsync(
                new { type = "error", message = "Bu oturuma erişim yetkiniz yok." }, ct);
            return;
        }

        // Yazılı kanalda bu bilgi mesaj listesine system mesajı olarak giriyor; sesli modda
        // mesaj listesi olmadığı için oturum talimatlarına ekleniyor (aynı kaynaktan üretilir).
        var identityContext = await _identityHint.BuildAsync(session, ct);
        await _client.ConfigureNativeSessionAsync(identityContext, ct);

        await channel.SendJsonAsync(new
        {
            type = "connected",
            mode = "native",
            sessionId = session.SessionId,
            model = _client.ModelName,
            voice = _client.Voice,
            tools = _client.NativeToolNames
        }, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var browserPump       = PumpBrowserAsync(channel, linked.Token);
        var eventPump         = HandleEventsAsync(channel, session, linked.Token);
        var inactivityWatcher = WatchInactivityAsync(channel, linked.Token);

        try
        {
            await Task.WhenAny(browserPump, eventPump, inactivityWatcher);
            await linked.CancelAsync();
            await Task.WhenAll(browserPump, eventPump, inactivityWatcher)
                .ContinueWith(_ => { }, TaskScheduler.Default);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RealtimeNative: pump hatası session={Sid}", session.SessionId);
        }
    }

    private async Task WatchInactivityAsync(IBrowserChannel channel, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                if (_endRequested) return;

                var elapsed = DateTime.UtcNow -
                              new DateTime(Interlocked.Read(ref _lastUserActivityTicks), DateTimeKind.Utc);
                if (elapsed < InactivityTimeout) continue;

                _logger.LogInformation("RealtimeNative: inactivity timeout ({Sec}s) — görüşme sonlandırılıyor",
                    (int)elapsed.TotalSeconds);
                _endRequested = true;
                _endReason = "idle_timeout";

                try { await channel.SendJsonAsync(new { type = "conversation_ended", reason = _endReason }, ct); }
                catch { /* best effort */ }
                try { await _client.CloseAsync("idle_timeout", ct); }
                catch { /* best effort */ }
                try { await channel.CloseAsync("idle_timeout", CancellationToken.None); }
                catch { /* best effort */ }
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    // ─── Browser → OpenAI ───

    private async Task PumpBrowserAsync(IBrowserChannel channel, CancellationToken ct)
    {
        await foreach (var msg in channel.ReceiveMessagesAsync(ct))
        {
            switch (msg.Kind)
            {
                case BrowserMessageKind.Closed:
                    return;

                case BrowserMessageKind.Binary:
                    if (!_assistantSpeaking)
                    {
                        Interlocked.Exchange(ref _lastUserActivityTicks, DateTime.UtcNow.Ticks);
                        await _client.SendAudioChunkAsync(msg.Data!, ct);
                    }
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
        catch { return; }

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
            _logger.LogWarning(ex, "RealtimeNative: control mesajı işlenirken hata type={Type}", type);
        }
    }

    // ─── OpenAI → Browser + tool dispatch ───

    private async Task HandleEventsAsync(IBrowserChannel channel, AgentSession session, CancellationToken ct)
    {
        var assistantTextBuilder    = new StringBuilder();
        var pendingCalls            = new List<(string CallId, string Name, string ArgsJson)>();
        var userTranscriptSent      = false;

        // Turun kullanıcı tarafı. Geçmişe "(sesli)" yazmak, konuşmanın YARISINI atmak demekti:
        // müşteri sesli modda ne sorduysa hiçbir yerde durmuyordu. Bunun iki sonucu vardı —
        // bot moduna geçildiğinde ajan önceki isteği bilmiyordu ve temsilci devraldığında
        // panelde müşterinin ne dediği görünmüyordu.
        string? lastUserTranscript = null;
        var bufferedAssistantDeltas = new List<string>();

        // Transkript yanıttan önce de sonra da gelebilir; tur yarıları item_id ile eşleştirilir
        // (bkz. VoiceTurnPairer). lastUserTranscript yalnızca kimliksiz akış için yedektir.
        var turns = new VoiceTurnPairer();

        // Girdi korumasını bekleme modu (finding-12): yanıt elle istenir. Önceki yanıt (ör. araç sonrası takip)
        // sürerken yeni istek gönderilirse sağlayıcı "aktif yanıt var" hatası verir ve istemcide görüşme koparıyordu —
        // istek yanıt bitene (ya da iptal edilene) kadar ertelenir.
        var responseActive = false;
        var responseWanted = false;

        async Task RequestResponseAsync()
        {
            if (responseActive)
            {
                responseWanted = true;
                return;
            }
            responseActive = true;   // ResponseCreated gelene kadar ikinci istek gönderilmesin
            await _client.RequestResponseAsync(ct);
        }

        async Task ReleaseDeferredResponseAsync()
        {
            responseActive = false;
            if (!responseWanted) return;
            responseWanted = false;
            await RequestResponseAsync();
        }

        try
        {
            await foreach (var evt in _client.ReceiveEventsAsync(ct))
            {
                switch (evt.EventType)
                {
                    case RealtimeServerEventType.InputAudioCommitted:
                        turns.AudioCommitted(evt.ItemId);
                        break;

                    case RealtimeServerEventType.ResponseCreated:
                        _assistantSpeaking = true;
                        responseActive = true;
                        // Tool sonucu üzerine gelen takip yanıtı aynı kullanıcı turudur: transkript
                        // zaten gönderildiyse akış devam eder; önceki yanıtın tamponlanmış metni de
                        // atılmaz (eskiden atılıyor, tool öncesi söylenen cümle ekranda hiç çıkmıyordu).
                        if (!turns.ResponseCreated())
                        {
                            userTranscriptSent = false;
                            bufferedAssistantDeltas.Clear();
                        }
                        break;

                    case RealtimeServerEventType.SpeechStarted:
                        await channel.SendJsonAsync(new { type = "speech_started", itemId = evt.ItemId }, ct);
                        break;

                    case RealtimeServerEventType.SpeechStopped:
                        await channel.SendJsonAsync(new { type = "speech_stopped", itemId = evt.ItemId }, ct);
                        break;

                    case RealtimeServerEventType.InputTranscriptDelta:
                        // Canlı altyazı: yalnızca konuşan kişinin ekranına gider. Geçmişe, duygu
                        // analizine, eşleştiriciye ya da onay kaydına GİRMEZ — parçaların birleşimi
                        // son metne eşit olmak zorunda değil; kalıcı kayıt tamamlanmış transkriptten.
                        if (!string.IsNullOrEmpty(evt.TextDelta))
                        {
                            await channel.SendJsonAsync(
                                new { type = "user_transcript_delta", itemId = evt.ItemId, text = evt.TextDelta }, ct);
                        }
                        break;

                    case RealtimeServerEventType.InputTranscriptCompleted:
                    {
                        var transcript = evt.Transcript;
                        if (string.IsNullOrWhiteSpace(transcript))
                        {
                            // Anlaşılır konuşma yok (gürültü/sessizlik). Tur yine de çözülmeli; aksi
                            // hâlde o öğeye ait yanıt transkript beklerken geçmişe yazılmaz.
                            await _recorder.RecordAsync(channel, session, turns.TranscriptFailed(evt.ItemId), ct);
                            break;
                        }

                        var guard = _inputGuard.Inspect(transcript);
                        if (guard.Verdict == InputGuardVerdict.Reject)
                        {
                            _logger.LogInformation("RealtimeNative: input guard reject session={Sid}", session.SessionId);
                            // Bekleme modunda model bu girdiye yanıt başlatmadı — kesilecek bir şey yok.
                            if (!_client.WaitsForInputGuard) await _client.SendInterruptAsync(ct);
                            await channel.SendJsonAsync(new { type = "user_transcript", itemId = evt.ItemId, text = transcript }, ct);
                            await channel.SendJsonAsync(
                                new { type = "error", message = guard.RejectionReason ?? "Mesaj işlenemedi." }, ct);
                            await _recorder.RecordAsync(channel, session, turns.TranscriptRejected(evt.ItemId), ct);
                            break;
                        }

                        // Koruma geçti: yanıtı hemen iste (kanala yazmadan önce — gecikmeyi büyütmesin).
                        if (_client.WaitsForInputGuard) await RequestResponseAsync();

                        Interlocked.Exchange(ref _lastUserActivityTicks, DateTime.UtcNow.Ticks);
                        await channel.SendJsonAsync(new { type = "user_transcript", itemId = evt.ItemId, text = transcript }, ct);
                        userTranscriptSent = true;
                        lastUserTranscript = transcript;
                        foreach (var delta in bufferedAssistantDeltas)
                            await channel.SendJsonAsync(new { type = "assistant_text_delta", text = delta }, ct);
                        bufferedAssistantDeltas.Clear();
                        // assistantTextBuilder burada TEMİZLENMEZ: transkript yanıtın ortasında
                        // gelebilir ve eskiden o ana kadarki asistan metni kayda hiç girmiyordu
                        // ("Siparişiniz kargoda." → "kargoda.").
                        await _recorder.RecordAsync(channel, session, turns.TranscriptArrived(evt.ItemId, transcript), ct);
                        break;
                    }

                    case RealtimeServerEventType.InputTranscriptFailed:
                        _logger.LogInformation(
                            "RealtimeNative: transkripsiyon başarısız session={Sid} item={Item} hata={Error}",
                            session.SessionId, evt.ItemId, evt.ErrorMessage);
                        await _recorder.RecordAsync(channel, session, turns.TranscriptFailed(evt.ItemId), ct);
                        // Bekleme modunda transkripsiyon hatası (sağlayıcı arızası) müşteriyi sessiz bırakmasın:
                        // model sesi doğrudan duyar ve yanıtlar (fail-open). Boş transkript (gürültü) yanıtlanmaz.
                        if (_client.WaitsForInputGuard) await RequestResponseAsync();
                        break;

                    case RealtimeServerEventType.AudioDelta:
                        _assistantSpeaking = true;
                        if (evt.AudioDelta is { Length: > 0 })
                            await channel.SendBinaryAsync(evt.AudioDelta, ct);
                        break;

                    case RealtimeServerEventType.AssistantTextDelta:
                    {
                        var delta = evt.TextDelta;
                        if (string.IsNullOrEmpty(delta)) break;
                        assistantTextBuilder.Append(delta);
                        if (userTranscriptSent)
                            await channel.SendJsonAsync(new { type = "assistant_text_delta", text = delta }, ct);
                        else
                            bufferedAssistantDeltas.Add(delta);
                        break;
                    }

                    case RealtimeServerEventType.AssistantTextDone:
                        if (!string.IsNullOrEmpty(evt.FullText) && assistantTextBuilder.Length == 0)
                            assistantTextBuilder.Append(evt.FullText);
                        break;

                    case RealtimeServerEventType.ToolCallReady:
                    {
                        if (string.IsNullOrWhiteSpace(evt.ToolCallId) || string.IsNullOrWhiteSpace(evt.ToolName)) break;

                        if (evt.ToolName == EndConversationToolName)
                        {
                            _endRequested = true;
                            try
                            {
                                var argNode = JsonNode.Parse(evt.ToolArguments ?? "{}") as JsonObject;
                                var reason = argNode?["reason"]?.GetValue<string>();
                                if (!string.IsNullOrWhiteSpace(reason)) _endReason = reason!;
                            }
                            catch { }
                        }

                        await channel.SendJsonAsync(
                            new { type = "tool_call", name = evt.ToolName, arguments = evt.ToolArguments }, ct);
                        pendingCalls.Add((evt.ToolCallId!, evt.ToolName!, evt.ToolArguments ?? "{}"));
                        break;
                    }

                    case RealtimeServerEventType.ResponseCancelled:
                        _assistantSpeaking = false;
                        pendingCalls.Clear();
                        assistantTextBuilder.Clear();
                        turns.ResponseCancelled();
                        await ReleaseDeferredResponseAsync();
                        break;

                    case RealtimeServerEventType.ResponseDone:
                    {
                        if (pendingCalls.Count > 0)
                        {
                            // DispatchToolCallsAsync takip yanıtını yalnızca görüşme bitmiyorsa ister.
                            turns.ToolCallsDispatched(followUpExpected: !_endRequested);
                            // Onay kaydına ve eskalasyona bu turun kullanıcı cümlesi yazılır; transkript
                            // henüz gelmediyse (eşleşme kimliği varken) yer tutucuya düşülür.
                            await DispatchToolCallsAsync(
                                channel, pendingCalls, session, turns.CurrentTranscript(lastUserTranscript), ct);
                            pendingCalls.Clear();
                            // Araç sonucu üzerine takip yanıtı istendi (görüşme bitmiyorsa) — yanıt sürüyor sayılır.
                            responseActive = !_endRequested;
                            break;
                        }

                        _assistantSpeaking = false;
                        foreach (var delta in bufferedAssistantDeltas)
                            await channel.SendJsonAsync(new { type = "assistant_text_delta", text = delta }, ct);
                        bufferedAssistantDeltas.Clear();

                        var finalText = assistantTextBuilder.ToString().Trim();
                        if (!string.IsNullOrEmpty(finalText))
                        {
                            await _recorder.RecordAsync(
                                channel, session, turns.ResponseCompleted(finalText, lastUserTranscript), ct);
                        }
                        await channel.SendJsonAsync(new { type = "assistant_text", text = finalText }, ct);
                        await channel.SendJsonAsync(new { type = "response_done" }, ct);
                        assistantTextBuilder.Clear();

                        if (_endRequested)
                        {
                            await channel.SendJsonAsync(
                                new { type = "conversation_ended", reason = _endReason }, ct);
                            try { await _client.CloseAsync("end_conversation", CancellationToken.None); } catch { }
                            try { await channel.CloseAsync("end_conversation", CancellationToken.None); } catch { }
                        }
                        else
                        {
                            await ReleaseDeferredResponseAsync();
                        }
                        break;
                    }

                    case RealtimeServerEventType.Error:
                        _logger.LogWarning("RealtimeNative: OpenAI error {Msg}", evt.ErrorMessage);
                        // Reddedilmiş bir response.create yanıt olayı üretmez; bayrak takılı kalırsa sonraki
                        // her istek sonsuza dek ertelenirdi.
                        responseActive = false;
                        await channel.SendJsonAsync(new { type = "error", message = evt.ErrorMessage }, ct);
                        break;

                    case RealtimeServerEventType.ConnectionClosed:
                        return;
                }
            }
        }
        finally
        {
            // Bağlantı kapanıyor (tarayıcı ayrıldı, görüşme bitti, iptal). Transkripti gelmemiş
            // turlar yer tutucuyla yazılır; tool çağrısıyla biten son yanıtın (ör. end_conversation
            // ile birlikte söylenen veda) metni de kaybolmasın. İptal edilmiş token'la değil —
            // kayıt, bağlantının kopmasından bağımsız tamamlanmalı.
            var trailing = assistantTextBuilder.ToString().Trim();
            if (!string.IsNullOrEmpty(trailing))
                turns.ResponseCompleted(trailing, lastUserTranscript);

            try { await _recorder.RecordAsync(channel, session, turns.DrainAll(), CancellationToken.None); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RealtimeNative: bekleyen sesli turlar kaydedilemedi session={Sid}",
                    session.SessionId);
            }
        }
    }

    private async Task DispatchToolCallsAsync(
        IBrowserChannel channel,
        List<(string CallId, string Name, string ArgsJson)> calls,
        AgentSession session,
        string? userQuery,
        CancellationToken ct)
    {
        var tasks = calls.Select(async c =>
        {
            string outputJson;
            try
            {
                outputJson = await _dispatcher.DispatchAsync(c.Name, c.ArgsJson, session, userQuery);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RealtimeNative: tool dispatch hatası tool={Tool}", c.Name);
                outputJson = VoiceToolDispatcher.ErrorJson(ex);
            }
            return new RealtimeToolResult(c.CallId, c.Name, outputJson);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
            await channel.SendJsonAsync(new { type = "tool_result", name = r.Name, output = r.OutputJson }, ct);

        await _client.SendToolResultsAsync(results, triggerNextResponse: !_endRequested, ct);
    }

    /// <inheritdoc cref="VoiceToolDispatcher.DispatchAsync"/>
    internal Task<string> DispatchToolAsync(string name, string argumentsJson, AgentSession session, string? userQuery)
        => _dispatcher.DispatchAsync(name, argumentsJson, session, userQuery);
}
