// Application/Services/RealtimeNativeService.cs
// IRealtimeNativeBridge driving port'unun Application katmanı implementasyonu.
// Native modda model kendi karar verir ve okuma-only tool'ları çağırır.
// Tool dispatch iş mantığı (hangi araçlar sesli modda kullanılabilir) burada kapsüllenir.
// Tarayıcı kanalı IBrowserChannel'da, realtime voice transport IRealtimeVoiceTransport'ta gizlenir.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary>
/// Native mod realtime oturumu — model konuşur, okuma-only tool'ları doğrudan çağırır.
/// Sipariş oluşturma / şikayet kaydı HITL gerektirdiği için bu kanalda bilinçli olarak yoktur.
/// </summary>
public sealed class RealtimeNativeService : IRealtimeNativeBridge
{
    // end_conversation aracı bu sabit üzerinden tanımlanır; adapter'daki tool adıyla tutarlı olmalı.
    private const string EndConversationToolName = "end_conversation";
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);

    private readonly IRealtimeVoiceTransport _client;
    private readonly ISessionManager _sessionManager;
    private readonly CustomerSupportToolsService _tools;
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
        ILogger<RealtimeNativeService> logger)
    {
        _client = client;
        _sessionManager = sessionManager;
        _tools = tools;
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
        var bufferedAssistantDeltas = new List<string>();

        await foreach (var evt in _client.ReceiveEventsAsync(ct))
        {
            switch (evt.EventType)
            {
                case RealtimeServerEventType.ResponseCreated:
                    _assistantSpeaking = true;
                    userTranscriptSent = false;
                    bufferedAssistantDeltas.Clear();
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

                    var guard = _inputGuard.Inspect(transcript);
                    if (guard.Verdict == InputGuardVerdict.Reject)
                    {
                        _logger.LogInformation("RealtimeNative: input guard reject session={Sid}", session.SessionId);
                        await _client.SendInterruptAsync(ct);
                        await channel.SendJsonAsync(new { type = "user_transcript", text = transcript }, ct);
                        await channel.SendJsonAsync(
                            new { type = "error", message = guard.RejectionReason ?? "Mesaj işlenemedi." }, ct);
                        break;
                    }

                    Interlocked.Exchange(ref _lastUserActivityTicks, DateTime.UtcNow.Ticks);
                    await channel.SendJsonAsync(new { type = "user_transcript", text = transcript }, ct);
                    userTranscriptSent = true;
                    foreach (var delta in bufferedAssistantDeltas)
                        await channel.SendJsonAsync(new { type = "assistant_text_delta", text = delta }, ct);
                    bufferedAssistantDeltas.Clear();
                    assistantTextBuilder.Clear();
                    break;
                }

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
                    break;

                case RealtimeServerEventType.ResponseDone:
                {
                    if (pendingCalls.Count > 0)
                    {
                        await DispatchToolCallsAsync(channel, pendingCalls, session, ct);
                        pendingCalls.Clear();
                        break;
                    }

                    _assistantSpeaking = false;
                    foreach (var delta in bufferedAssistantDeltas)
                        await channel.SendJsonAsync(new { type = "assistant_text_delta", text = delta }, ct);
                    bufferedAssistantDeltas.Clear();

                    var finalText = assistantTextBuilder.ToString().Trim();
                    if (!string.IsNullOrEmpty(finalText))
                        _chatBridge.RecordBotExchange(session.SessionId, "(sesli)", finalText);
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
                    break;
                }

                case RealtimeServerEventType.Error:
                    _logger.LogWarning("RealtimeNative: OpenAI error {Msg}", evt.ErrorMessage);
                    await channel.SendJsonAsync(new { type = "error", message = evt.ErrorMessage }, ct);
                    break;

                case RealtimeServerEventType.ConnectionClosed:
                    return;
            }
        }
    }

    private async Task DispatchToolCallsAsync(
        IBrowserChannel channel,
        List<(string CallId, string Name, string ArgsJson)> calls,
        AgentSession session,
        CancellationToken ct)
    {
        var tasks = calls.Select(async c =>
        {
            string outputJson;
            try
            {
                outputJson = DispatchTool(c.Name, c.ArgsJson, session.State.AuthenticatedCustomerId ?? "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RealtimeNative: tool dispatch hatası tool={Tool}", c.Name);
                outputJson = JsonSerializer.Serialize(new
                {
                    success = false,
                    error = new { code = "TOOL_DISPATCH_ERROR", message = ex.Message }
                }, ToolResultJsonOpts);
            }
            return new RealtimeToolResult(c.CallId, c.Name, outputJson);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var r in results)
            await channel.SendJsonAsync(new { type = "tool_result", name = r.Name, output = r.OutputJson }, ct);

        await _client.SendToolResultsAsync(results, triggerNextResponse: !_endRequested, ct);
    }

    private string DispatchTool(string name, string argumentsJson, string authenticatedCustomerId)
    {
        ToolResult result;
        try
        {
            var args = JsonNode.Parse(argumentsJson) as JsonObject ?? new JsonObject();

            // customer_id LLM argümanından ASLA okunmaz — sadece login'li kullanıcının
            // JWT-doğrulanmış kimliği (bkz. DispatchToolCallsAsync → session.State.AuthenticatedCustomerId)
            // kullanılır; aksi halde model başka bir müşterinin sipariş geçmişini isteyebilirdi.
            result = name switch
            {
                "product_inquiry_tool" => _tools.ProductInquiryTool(GetString(args, "product_name") ?? ""),
                "product_list_tool"    => _tools.ProductListTool(GetString(args, "category")),
                "order_status_tool"    => _tools.OrderStatusTool(GetString(args, "order_id") ?? "", authenticatedCustomerId),
                "get_last_order_tool"  => _tools.GetLastOrderTool(authenticatedCustomerId),
                "get_all_orders_tool"  => _tools.GetAllOrdersTool(authenticatedCustomerId),
                EndConversationToolName =>
                    ToolResult.Ok("Görüşme sonlandırılıyor.", new
                    {
                        ended  = true,
                        reason = GetString(args, "reason") ?? "user_farewell"
                    }),
                // HITL gerektiren tool'lar sesli modda bilinçli olarak engellidir.
                "order_placement_tool" or "order_cancel_tool" or "return_request_tool" or "complaint_registration_tool" =>
                    ToolResult.SystemError("FORBIDDEN_IN_VOICE",
                        "Bu işlem güvenlik adımları gerektirir; yazılı sohbet üzerinden yapılmalıdır."),
                _ => ToolResult.SystemError("UNKNOWN_TOOL", $"'{name}' bu modda mevcut değil.")
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "RealtimeNative: tool argümanları parse edilemedi name={Name}", name);
            result = ToolResult.SystemError("INVALID_ARGS", "Tool argümanları geçersiz JSON.");
        }

        return JsonSerializer.Serialize(result, ToolResultJsonOpts);
    }

    private static string? GetString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null) return null;
        return node.GetValue<string>();
    }

    private static readonly JsonSerializerOptions ToolResultJsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
