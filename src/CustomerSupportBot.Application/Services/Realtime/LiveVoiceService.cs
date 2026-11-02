// Application/Services/Realtime/LiveVoiceService.cs
// IRealtimeNativeBridge'in GPT-Live motoru: tam çift yönlü görüşme, tool'lar arka plan modelinden gelir.

using System.Text.Json.Nodes;
using System.Threading.Channels;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Realtime;

/// <summary>
/// Sesli görüşmenin GPT-Live motoru. Tarayıcı sözleşmesi ve iş kuralları <see cref="RealtimeNativeService"/> ile aynıdır; fark sağlayıcının
/// çalışma biçimindedir ve bu sınıfın neredeyse tüm mantığı o farktan gelir:
///
/// <list type="bullet">
/// <item><b>Ses hiç kesilmez.</b> Oturum zaman çizelgesi gelen sesle ilerler. Asistan konuşurken de, kapanışta da ses akar; çıkışta da
/// sessiz anlarda paket gelir, bu yüzden "konuşuyor" bilgisi paketin varlığından değil içeriğinden okunur.</item>
/// <item><b>Tur yoktur.</b> Sağlayıcı yalnızca transkript parçası verir; turlar <see cref="LiveTurnAssembler"/> ile çıkarılır, ekranın
/// beklediği olaylar buradan üretilir.</item>
/// <item><b>Yanıt durdurulamaz, bekletilemez.</b> Girdi koruması ancak tepkisel olabilir: konuşma devam ederken metin incelenir, reddedilirse
/// tool'lar uygulamada engellenir, sesin tarayıcıya akışı kısa süre kesilir ve modele talimat gönderilir.</item>
/// <item><b>Ücret süreye bağlıdır.</b> Sessizlik de ücretli olduğundan boşta kalma ve üst süre sınırları zorunludur; etkinlik sesten değil
/// konuşma metninden ölçülür, çünkü tarayıcı sessizlikte de ses gönderir.</item>
/// </list>
///
/// Tüm durum tek bir olay döngüsünde değişir (<see cref="ProcessAsync"/>): sağlayıcı olayları, zamanlayıcı, tool sonuçları ve tarayıcı
/// komutları kuyruğa girer. Böylece <see cref="LiveTurnAssembler"/> ve tarayıcı soketi (aynı anda tek gönderim) kilit gerektirmez.
/// Tool'lar döngü dışında çalışır: tool sürerken konuşma ve ses durmaz.
/// </summary>
public sealed class LiveVoiceService : IRealtimeNativeBridge
{
    private const string Provider = "OpenAI";

    /// <summary>100 ms sessizlik (24 kHz, PCM16): kapanışta oturum zaman çizelgesinin ilerlemesi için.</summary>
    private static readonly byte[] SilentFrame = new byte[4800];

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Asistan sesi bu kadar yakın zamanda geldiyse hâlâ konuşuyor sayılır (paketler 100 ms'de bir gelir).</summary>
    private const int SpeakingWindowMs = 400;
    private const int BargeInMinWords = 2;

    /// <summary>Arka plan bittikten sonra bu kadar sessizlikten sonra gelen metin, önceki cümlenin devamı değil cevabın başlangıcıdır.</summary>
    private const int AnswerGapMs = 500;

    /// <summary>Reddedilen girdiden sonra modelin ağzından çıkacak sesin tarayıcıya gitmediği süre.</summary>
    private const int GuardMuteMs = 1500;
    private const int InterruptMuteMs = 600;

    /// <summary>Ses maliyeti bütçeye bu aralıklarla yazılır; her kullanım olayında sayaca gidilmez.</summary>
    private const double BillingStepSeconds = 10;

    private const string GuardInstruction =
        "Konuşmayı hemen bırak. Az önceki isteği yerine getirme ve hiçbir işlem başlatma. Bu konuda yardımcı olamayacağını kısaca söyle ve dinle.";
    private const string InterruptInstruction = "Konuşmayı hemen bırak ve müşteriyi dinle.";
    private const string BackendFailedInstruction =
        "Bu bilgiye şu anda ulaşılamıyor. Müşteriye kısaca özür dile, sonucu tahmin etme ve tekrar denemesini ya da temsilciye bağlanmayı öner.";

    private const string GenericStartError = "Sesli asistan şu anda başlatılamadı. Lütfen yazılı sohbeti kullanın.";

    private readonly ILiveVoiceTransport _client;
    private readonly ISessionManager _sessionManager;
    private readonly VoiceToolDispatcher _dispatcher;
    private readonly VoiceTurnRecorder _recorder;
    private readonly IInputGuard _inputGuard;
    private readonly CustomerIdentityHintBuilder _identityHint;
    private readonly IAppDistributedLock _sessionLock;
    private readonly LiveVoiceOptions _options;
    private readonly ILogger<LiveVoiceService> _logger;
    private readonly TimeProvider _clock;
    private readonly ILlmSpendGuard? _spendGuard;
    private readonly ILlmCallPersistencePort? _usage;
    private readonly ICostCalculatorPort? _costs;
    private readonly LiveTurnAssembler _assembler;

    private readonly Channel<Input> _inputs = Channel.CreateUnbounded<Input>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Channel<ToolCall> _toolCalls = Channel.CreateUnbounded<ToolCall>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Oturumdaki açık delegasyonlar: kimlik → son tamamlanmadan bu yana gelen tool çağrısı.</summary>
    private readonly Dictionary<string, int> _delegations = new();
    private readonly HashSet<string> _blockedDelegations = new();
    private readonly HashSet<string> _countedResponses = new();
    private readonly List<LlmCallRecord> _backendCalls = new();

    private volatile bool _started;
    private volatile bool _closing;

    private long _connectedAtMs;
    private long _startedAtMs;
    private long _lastActivityMs;
    private long _lastSpeechMs;
    private long _lastUserDeltaMs;
    private long _lastAssistantDeltaMs;
    private long _awaitingAnswerSinceMs;
    private long _dropOutputUntilMs;
    private long _endRequestedAtMs;
    private string _endReason = "user_farewell";
    private string? _lastUserText;
    private bool _bargeInSent;
    private bool _guardTripped;
    private bool _blockNewDelegations;
    private int _toolsInFlight;
    private double _voiceSeconds;
    private double _billedSeconds;
    private bool _endAnnounced;

    /// <summary>Tur, boşta kalma ve süre denetiminin aralığı.</summary>
    internal TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Kapanışta sessizlik gönderme aralığı.</summary>
    internal TimeSpan SilenceInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Son kullanım bilgisi için <c>session.closed</c> beklenen en uzun süre.</summary>
    internal TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public LiveVoiceService(
        ILiveVoiceTransport client,
        ISessionManager sessionManager,
        CustomerSupportToolsService tools,
        IInputGuard inputGuard,
        IChatBridge chatBridge,
        CustomerIdentityHintBuilder identityHint,
        IAppDistributedLock sessionLock,
        IOptions<LiveVoiceOptions> options,
        ILogger<LiveVoiceService> logger,
        SessionStateService? sessionState = null,
        IEscalationSink? escalations = null,
        SideEffectApprovalGate? approvalGate = null,
        ILlmSpendGuard? spendGuard = null,
        ILlmCallPersistencePort? usage = null,
        ICostCalculatorPort? costs = null,
        TimeProvider? clock = null)
    {
        _client = client;
        _sessionManager = sessionManager;
        _dispatcher = new VoiceToolDispatcher(tools, logger, escalations, approvalGate);
        _recorder = new VoiceTurnRecorder(chatBridge, sessionManager, logger, sessionState, escalations);
        _inputGuard = inputGuard;
        _identityHint = identityHint;
        _sessionLock = sessionLock;
        _options = options.Value;
        _logger = logger;
        _spendGuard = spendGuard;
        _usage = usage;
        _costs = costs;
        _clock = clock ?? TimeProvider.System;
        _assembler = new LiveTurnAssembler(_options.UserPauseMs, _options.AssistantPauseMs);
    }

    private abstract record Input;
    private sealed record ProviderEvent(LiveServerEvent Event) : Input;
    private sealed record Tick : Input;
    private sealed record ToolDone(string CallId, string Name, string OutputJson) : Input;
    private sealed record BrowserControl(string Type) : Input;
    private sealed record BrowserGone : Input;
    private sealed record BudgetExceeded : Input;

    private sealed record ToolCall(string CallId, string Name, string Arguments, string? UserQuery);

    private long NowMs => _clock.GetUtcNow().ToUnixTimeMilliseconds();

    public async Task RunAsync(
        IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)
    {
        if (!_client.IsEnabled)
        {
            await channel.SendJsonAsync(new { type = "error", message = "Sesli asistan kapalı." }, ct);
            return;
        }

        if (!await _client.TryConnectAsync(ct))
        {
            await channel.SendJsonAsync(new { type = "error", message = GenericStartError }, ct);
            return;
        }

        // Yazılı sohbet ve Realtime motoruyla aynı atomik kimlik bağlama (bkz. RealtimeNativeService.RunAsync).
        var session = await SessionIdentityBinder.BindAtomicallyAsync(
            sessionId, authenticatedCustomerId, _sessionManager, _sessionLock, ct);
        if (session is null)
        {
            _logger.LogWarning("LiveVoice: oturum başka bir müşteriye ait, bağlantı reddedildi session={Sid}", sessionId);
            await channel.SendJsonAsync(new { type = "error", message = "Bu oturuma erişim yetkiniz yok." }, ct);
            return;
        }

        var identity = await _identityHint.BuildAsync(session, ct);
        var history = await _sessionManager.GetHistoryAsync(session.SessionId, ct);
        _connectedAtMs = NowMs;
        await _client.StartSessionAsync(new LiveSessionSetup(identity, history), ct);

        // Sağlayıcı bağlantısı tarayıcıdan bağımsız yaşar: tarayıcı gittikten sonra da session.closed okunmalı, yoksa son kullanım kaybolur.
        using var providerCts = new CancellationTokenSource();
        using var browserCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var provider = ReadProviderAsync(providerCts.Token);
        var tools = RunToolsAsync(session, providerCts.Token);
        var ticker = TickAsync(providerCts.Token);
        var browser = PumpBrowserAsync(channel, browserCts.Token);
        var loop = ProcessAsync(channel, session, providerCts);

        try
        {
            if (await Task.WhenAny(loop, browser) != loop)
            {
                _inputs.Writer.TryWrite(new BrowserGone());
                await Task.WhenAny(loop, Task.Delay(CloseTimeout, CancellationToken.None));
            }
        }
        finally
        {
            await providerCts.CancelAsync();
            _toolCalls.Writer.TryComplete();
            _inputs.Writer.TryComplete();
            await Task.WhenAll(provider, tools, ticker, loop).ContinueWith(_ => { }, TaskScheduler.Default);

            await FinishAsync(channel, session);

            // Önce kapat, sonra iptal et: bekleyen okumayı iptal etmek soketi düşürür ve tarayıcı temiz kapanış görmez.
            await CloseBrowserAsync(channel);
            await browserCts.CancelAsync();
            await browser.ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private static async Task CloseBrowserAsync(IBrowserChannel channel)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await channel.CloseAsync("session_end", timeout.Token);
        }
        catch { /* best effort */ }
    }

    // ─── Girdi kaynakları ───

    private async Task ReadProviderAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _client.ReceiveEventsAsync(ct))
                _inputs.Writer.TryWrite(new ProviderEvent(evt));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogWarning(ex, "LiveVoice: sağlayıcı okuma hatası"); }
        _inputs.Writer.TryWrite(new ProviderEvent(new LiveServerEvent(LiveServerEventType.ConnectionClosed)));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TickInterval, ct);
                _inputs.Writer.TryWrite(new Tick());
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task PumpBrowserAsync(IBrowserChannel channel, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in channel.ReceiveMessagesAsync(ct))
            {
                switch (msg.Kind)
                {
                    case BrowserMessageKind.Closed:
                        // Sağlayıcının kapanışı saniyeler sürer; tarayıcının kapatma isteği onu beklememeli.
                        await CloseBrowserAsync(channel);
                        return;

                    case BrowserMessageKind.Binary:
                        // Kapı yok: tam çift yönlü modelde mikrofon asistan konuşurken de akar.
                        if (_started && !_closing) await _client.SendAudioChunkAsync(msg.Data!, ct);
                        break;

                    case BrowserMessageKind.Text:
                        var type = ControlType(msg.AsText());
                        if (type is not null) _inputs.Writer.TryWrite(new BrowserControl(type));
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogInformation(ex, "LiveVoice: tarayıcı akışı kapandı"); }
    }

    private static string? ControlType(string json)
    {
        try { return JsonNode.Parse(json)?["type"]?.GetValue<string>(); }
        catch { return null; }
    }

    private async Task RunToolsAsync(AgentSession session, CancellationToken ct)
    {
        try
        {
            await foreach (var call in _toolCalls.Reader.ReadAllAsync(ct))
            {
                string output;
                try
                {
                    output = await _dispatcher.DispatchAsync(call.Name, call.Arguments, session, call.UserQuery);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LiveVoice: tool dispatch hatası tool={Tool}", call.Name);
                    output = VoiceToolDispatcher.ErrorJson(ex);
                }
                _inputs.Writer.TryWrite(new ToolDone(call.CallId, call.Name, output));
            }
        }
        catch (OperationCanceledException) { }
    }

    // ─── Olay döngüsü ───

    private async Task ProcessAsync(IBrowserChannel channel, AgentSession session, CancellationTokenSource providerCts)
    {
        var ct = providerCts.Token;
        try
        {
            await foreach (var input in _inputs.Reader.ReadAllAsync(ct))
            {
                var finished = input switch
                {
                    ProviderEvent e => await HandleProviderEventAsync(channel, session, e.Event, ct),
                    Tick => await HandleTickAsync(channel, session, ct),
                    ToolDone t => await HandleToolDoneAsync(channel, t, ct),
                    BrowserControl c => await HandleBrowserControlAsync(c.Type, ct),
                    BrowserGone => await BeginCloseAsync(providerCts),
                    BudgetExceeded => await EndConversationAsync(channel, "budget", ct),
                    _ => false
                };
                if (finished) return;
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <returns><c>true</c>: görüşme bitti, döngü sonlanmalı.</returns>
    private async Task<bool> HandleProviderEventAsync(
        IBrowserChannel channel, AgentSession session, LiveServerEvent evt, CancellationToken ct)
    {
        var now = NowMs;
        switch (evt.EventType)
        {
            case LiveServerEventType.SessionStarted:
                _started = true;
                _startedAtMs = now;
                _lastActivityMs = now;
                await SendAsync(channel, new
                {
                    type = "connected",
                    mode = "live",
                    sessionId = session.SessionId,
                    model = _client.ModelName,
                    voice = _client.Voice,
                    tools = _client.NativeToolNames
                }, ct);
                break;

            case LiveServerEventType.AudioDelta:
                if (!evt.HasSpeech) break;
                _lastSpeechMs = now;
                _lastActivityMs = now;
                _assembler.AssistantSpeech(now);
                if (now >= _dropOutputUntilMs && evt.AudioDelta is { Length: > 0 } audio)
                    await SendBinaryAsync(channel, audio, ct);
                break;

            case LiveServerEventType.InputTranscriptDelta:
                await HandleUserDeltaAsync(channel, session, evt.TextDelta, now, ct);
                break;

            case LiveServerEventType.OutputTranscriptDelta:
                if (string.IsNullOrEmpty(evt.TextDelta)) break;
                _lastActivityMs = now;
                if (_awaitingAnswerSinceMs > 0 && now - _lastAssistantDeltaMs >= Math.Min(AnswerGapMs, _options.AssistantPauseMs))
                {
                    _awaitingAnswerSinceMs = 0;
                    UpdateBackendBusy();
                }
                _lastAssistantDeltaMs = now;
                await ApplyAsync(channel, session, _assembler.AssistantDelta(evt.TextDelta, now), ct);
                break;

            case LiveServerEventType.DelegationCreated:
                var id = evt.DelegationId ?? "";
                _delegations.TryAdd(id, 0);
                if (_blockNewDelegations) _blockedDelegations.Add(id);
                UpdateBackendBusy();
                break;

            case LiveServerEventType.ToolCallReady:
                await HandleToolCallAsync(channel, evt, now, ct);
                break;

            case LiveServerEventType.BackendResponseCompleted:
                CountBackendUsage(session, evt);
                CompleteBackendResponse(evt.DelegationId ?? "", now);
                break;

            case LiveServerEventType.BackendResponseFailed:
                _logger.LogWarning("LiveVoice: arka plan yanıtı başarısız session={Sid} hata={Error}", session.SessionId, evt.ErrorMessage);
                CountBackendUsage(session, evt);
                _delegations.Remove(evt.DelegationId ?? "");
                UpdateBackendBusy();
                await _client.SendInstructionAsync(BackendFailedInstruction, ct);
                break;

            case LiveServerEventType.UsageUpdated:
                TrackVoiceUsage(evt.UsageSeconds, session.SessionId);
                break;

            case LiveServerEventType.Error:
                _logger.LogWarning("LiveVoice: sağlayıcı hatası kod={Code} mesaj={Message}", evt.ErrorCode, evt.ErrorMessage);
                if (_started) break;
                // Sağlayıcının metni müşteriye gösterilmez: hesap ve yapılandırma ayrıntısı taşıyabilir.
                await SendAsync(channel, new { type = "error", message = GenericStartError }, ct);
                return true;

            case LiveServerEventType.SessionClosed:
                if (evt.UsageSeconds is { } seconds && seconds > _voiceSeconds) _voiceSeconds = seconds;
                await AnnounceProviderCloseAsync(channel, evt.CloseReason, ct);
                return true;

            case LiveServerEventType.ConnectionClosed:
                await AnnounceProviderCloseAsync(channel, "connection_lost", ct);
                return true;
        }
        return false;
    }

    private async Task HandleUserDeltaAsync(
        IBrowserChannel channel, AgentSession session, string? text, long now, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_guardTripped)
        {
            // Reddedilen konuşmanın devamı: ayrı tur açılırsa cümlenin kalanı denetimden kaçardı.
            _lastUserDeltaMs = now;
            return;
        }

        var hasWords = !string.IsNullOrWhiteSpace(text);
        if (hasWords)
        {
            _lastActivityMs = now;
            _lastUserDeltaMs = now;
            // Engel, duraklamayla değil yeni kabul edilen konuşmayla kalkar: model işi konuşma bittikten yaklaşık 1 sn sonra devreder,
            // yani reddedilen isteğin delegasyonu duraklama süresi dolduktan sonra da gelebilir.
            _blockNewDelegations = false;
        }

        await ApplyAsync(channel, session, _assembler.UserDelta(text, now), ct);
        if (!hasWords) return;

        if (await InspectUserAsync(channel, session, now, ct)) return;

        // Sağlayıcı söze girmeyi bildirmez ve modeli kendisi durdurur; burada yalnızca tarayıcıda biriken ses boşaltılır.
        if (!_bargeInSent && now - _lastSpeechMs <= SpeakingWindowMs && _assembler.OpenSegmentWords >= BargeInMinWords)
        {
            _bargeInSent = true;
            await SendAsync(channel, new { type = "barge_in" }, ct);
        }
    }

    /// <summary>Metni büyüdükçe inceler: yanıt bekletilemediği için mümkün olan en erken anda davranmak gerekir.</summary>
    private async Task<bool> InspectUserAsync(IBrowserChannel channel, AgentSession session, long now, CancellationToken ct)
    {
        var text = _assembler.CurrentUserText;
        if (text is null) return false;

        var result = _inputGuard.Inspect(text);
        if (result.Verdict != InputGuardVerdict.Reject) return false;

        _logger.LogInformation("LiveVoice: input guard reject session={Sid}", session.SessionId);
        _guardTripped = true;
        _blockNewDelegations = true;
        _blockedDelegations.UnionWith(_delegations.Keys);
        _dropOutputUntilMs = now + GuardMuteMs;
        await ApplyAsync(channel, session, _assembler.RejectCurrentTurn(), ct);
        await SendAsync(channel, new { type = "barge_in", force = true }, ct);
        await SendAsync(channel, new { type = "error", message = result.RejectionReason ?? "Mesaj işlenemedi." }, ct);
        await _client.SendInstructionAsync(GuardInstruction, ct);
        return true;
    }

    private async Task HandleToolCallAsync(IBrowserChannel channel, LiveServerEvent evt, long now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evt.ToolCallId) || string.IsNullOrWhiteSpace(evt.ToolName)) return;

        var delegation = evt.DelegationId ?? "";
        _delegations[delegation] = _delegations.GetValueOrDefault(delegation) + 1;
        UpdateBackendBusy();
        _lastActivityMs = now;
        _toolsInFlight++;

        if (_blockedDelegations.Contains(delegation))
        {
            // Sonuç yine de dönmeli: eksik sonuç arka planı "tool_outputs_required" hatasında bırakır.
            _inputs.Writer.TryWrite(new ToolDone(evt.ToolCallId, evt.ToolName, BlockedToolJson));
            return;
        }

        if (evt.ToolName == VoiceToolDispatcher.EndConversationToolName)
        {
            _endRequestedAtMs = now;
            _endReason = EndReason(evt.ToolArguments) ?? _endReason;
        }

        await SendAsync(channel, new { type = "tool_call", name = evt.ToolName, arguments = evt.ToolArguments }, ct);
        _toolCalls.Writer.TryWrite(new ToolCall(
            evt.ToolCallId, evt.ToolName, evt.ToolArguments ?? "{}", _assembler.CurrentUserText ?? _lastUserText));
    }

    private const string BlockedToolJson =
        """{"success":false,"error":{"code":"BLOCKED","message":"Bu istek güvenlik nedeniyle işlenmedi."}}""";

    private static string? EndReason(string? argumentsJson)
    {
        try { return JsonNode.Parse(argumentsJson ?? "{}")?["reason"]?.GetValue<string>(); }
        catch { return null; }
    }

    private async Task<bool> HandleToolDoneAsync(IBrowserChannel channel, ToolDone done, CancellationToken ct)
    {
        await SendAsync(channel, new { type = "tool_result", name = done.Name, output = done.OutputJson }, ct);
        _toolsInFlight--;
        await _client.SendToolResultsAsync(
            [new RealtimeToolResult(done.CallId, done.Name, done.OutputJson)], continueBackend: _toolsInFlight == 0, ct);
        return false;
    }

    private async Task<bool> HandleBrowserControlAsync(string type, CancellationToken ct)
    {
        switch (type)
        {
            case "interrupt":
                _dropOutputUntilMs = NowMs + InterruptMuteMs;
                await _client.SendInstructionAsync(InterruptInstruction, ct);
                break;
            case "stop":
                _inputs.Writer.TryWrite(new BrowserGone());
                break;
        }
        return false;
    }

    private async Task<bool> HandleTickAsync(IBrowserChannel channel, AgentSession session, CancellationToken ct)
    {
        var now = NowMs;

        if (!_started)
        {
            if (now - _connectedAtMs < StartTimeout.TotalMilliseconds) return false;
            _logger.LogWarning("LiveVoice: oturum zamanında başlamadı session={Sid}", session.SessionId);
            await SendAsync(channel, new { type = "error", message = GenericStartError }, ct);
            return true;
        }

        if (_guardTripped && now - _lastUserDeltaMs >= _options.UserPauseMs) _guardTripped = false;

        if (_awaitingAnswerSinceMs > 0 && now - _awaitingAnswerSinceMs >= _options.SpokenAnswerWaitSeconds * 1000)
        {
            _awaitingAnswerSinceMs = 0;
            UpdateBackendBusy();
        }

        await ApplyAsync(channel, session, _assembler.Tick(now), ct);
        if (_closing || _endAnnounced) return false;

        if (now - _startedAtMs >= _options.MaxSessionSeconds * 1000)
            return await EndConversationAsync(channel, "max_duration", ct);

        var backendIdle = _delegations.Count == 0;
        if (_endRequestedAtMs > 0)
        {
            var settled = backendIdle && now - Math.Max(_endRequestedAtMs, _lastSpeechMs) >= _options.AssistantPauseMs;
            if (settled || now - _endRequestedAtMs >= _options.EndGraceSeconds * 1000)
                return await EndConversationAsync(channel, _endReason, ct);
            return false;
        }

        if (backendIdle && now - _lastActivityMs >= _options.IdleTimeoutSeconds * 1000)
        {
            _logger.LogInformation("LiveVoice: boşta kalma süresi doldu session={Sid}", session.SessionId);
            return await EndConversationAsync(channel, "idle_timeout", ct);
        }
        return false;
    }

    private async Task ApplyAsync(
        IBrowserChannel channel, AgentSession session, IReadOnlyList<LiveTurnSignal> signals, CancellationToken ct)
    {
        foreach (var signal in signals)
        {
            switch (signal)
            {
                case LiveTurnSignal.UserStarted started:
                    _bargeInSent = false;
                    await SendAsync(channel, new { type = "speech_started", itemId = started.ItemId }, ct);
                    break;

                case LiveTurnSignal.UserDelta delta:
                    await SendAsync(channel, new { type = "user_transcript_delta", itemId = delta.ItemId, text = delta.Text }, ct);
                    break;

                case LiveTurnSignal.UserCompleted completed:
                    _lastUserText = completed.Text;
                    await SendAsync(channel, new { type = "speech_stopped", itemId = completed.ItemId }, ct);
                    await SendAsync(channel, new { type = "user_transcript", itemId = completed.ItemId, text = completed.Text }, ct);
                    break;

                case LiveTurnSignal.AssistantDelta delta:
                    await SendAsync(channel, new { type = "assistant_text_delta", text = delta.Text }, ct);
                    break;

                case LiveTurnSignal.AssistantCompleted completed:
                    await SendAsync(channel, new { type = "assistant_text", text = completed.Text }, ct);
                    await SendAsync(channel, new { type = "response_done" }, ct);
                    break;

                case LiveTurnSignal.TurnReady ready:
                    await RecordTurnAsync(channel, session, ready.Turn, ct);
                    break;
            }
        }
    }

    private async Task RecordTurnAsync(IBrowserChannel channel, AgentSession session, VoiceTurn turn, CancellationToken ct)
    {
        try { await _recorder.RecordAsync(channel, session, [turn], ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kayıt hatası süren görüşmeyi kesmemeli.
            _logger.LogWarning(ex, "LiveVoice: tur kaydedilemedi session={Sid}", session.SessionId);
        }
    }

    // ─── Arka plan işi ───

    private void CompleteBackendResponse(string delegation, long now)
    {
        if (!_delegations.TryGetValue(delegation, out var calls)) return;

        // Tool çağıran yanıt tamamlandı ama sonuçla devam eden yanıt gelecek: iş bitmedi.
        if (calls > 0)
        {
            _delegations[delegation] = 0;
            return;
        }

        _delegations.Remove(delegation);
        // Cevap henüz söylenmedi: model susarken tur kapanırsa cevap "(sesli)" kullanıcılı ayrı bir tura düşerdi.
        _awaitingAnswerSinceMs = now;
        UpdateBackendBusy();
    }

    private void UpdateBackendBusy() => _assembler.BackendBusy = _delegations.Count > 0 || _awaitingAnswerSinceMs > 0;

    // ─── Kullanım ve maliyet ───

    private void TrackVoiceUsage(double? seconds, string sessionId)
    {
        if (seconds is not { } total || total <= _voiceSeconds) return;

        _voiceSeconds = total;
        if (total - _billedSeconds >= BillingStepSeconds) BillVoice(sessionId);
    }

    private void BillVoice(string sessionId)
    {
        var unbilled = _voiceSeconds - _billedSeconds;
        if (unbilled <= 0) return;
        _billedSeconds = _voiceSeconds;
        SpendInBackground(VoiceCost(unbilled), sessionId);
    }

    /// <summary>Son kayıt beklenir: görüşme bittikten sonra arka planda kalırsa uygulama kapanışında maliyet kaybolabilirdi.</summary>
    private async Task BillFinalVoiceAsync(string sessionId)
    {
        var unbilled = _voiceSeconds - _billedSeconds;
        if (unbilled <= 0 || _spendGuard is null) return;

        _billedSeconds = _voiceSeconds;
        try { await _spendGuard.RecordAsync(VoiceCost(unbilled), sessionId); }
        catch (Exception ex) { _logger.LogWarning(ex, "LiveVoice: son harcama kaydı başarısız session={Sid}", sessionId); }
    }

    private decimal VoiceCost(double seconds) => (decimal)seconds / 60m * _options.PricePerMinuteUsd;

    private void CountBackendUsage(AgentSession session, LiveServerEvent evt)
    {
        // Aynı yanıt iki kez tamamlandı olayı üretebiliyor; maliyet yanıt kimliğiyle bir kez sayılır.
        if (evt.ResponseId is null || !_countedResponses.Add(evt.ResponseId)) return;

        var model = evt.BackendModel ?? "unknown";
        var cost = _costs?.CalculateCost(model, Provider, (int)evt.InputTokens, (int)evt.OutputTokens) ?? 0m;
        _backendCalls.Add(new LlmCallRecord(
            model, Provider, evt.InputTokens, evt.OutputTokens, cost, 0, _clock.GetUtcNow().UtcDateTime, session.SessionId));
        SpendInBackground(cost, session.SessionId);
    }

    /// <summary>Sayaç (Redis) olay döngüsünü bekletmez: ses aktarımı bütçe kaydı yüzünden gecikmemeli.</summary>
    private void SpendInBackground(decimal cost, string sessionId)
    {
        if (_spendGuard is null || cost <= 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _spendGuard.RecordAsync(cost, sessionId);
                if (await _spendGuard.CheckAsync(sessionId) is not null)
                    _inputs.Writer.TryWrite(new BudgetExceeded());
            }
            catch (Exception ex) { _logger.LogWarning(ex, "LiveVoice: harcama kaydı başarısız"); }
        });
    }

    // ─── Kapanış ───

    private async Task<bool> EndConversationAsync(IBrowserChannel channel, string reason, CancellationToken ct)
    {
        if (_endAnnounced) return false;
        _endAnnounced = true;
        await SendAsync(channel, new { type = "conversation_ended", reason }, ct);
        await RequestCloseAsync(ct);
        return false;
    }

    private async Task<bool> BeginCloseAsync(CancellationTokenSource providerCts)
    {
        await RequestCloseAsync(providerCts.Token);
        return false;
    }

    private async Task RequestCloseAsync(CancellationToken ct)
    {
        if (_closing) return;
        _closing = true;
        if (!_started)
        {
            _inputs.Writer.TryWrite(new ProviderEvent(new LiveServerEvent(LiveServerEventType.ConnectionClosed)));
            return;
        }

        try { await _client.RequestCloseAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "LiveVoice: kapanış isteği gönderilemedi");
        }
        // Ölçüldü: ses kesilirse zaman çizelgesi durur ve session.closed hiç gelmez.
        _ = KeepTimelineMovingAsync(ct);
    }

    private async Task KeepTimelineMovingAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _client.SendAudioChunkAsync(SilentFrame, ct);
                await Task.Delay(SilenceInterval, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogDebug(ex, "LiveVoice: kapanış sessizliği durdu"); }
    }

    private async Task AnnounceProviderCloseAsync(IBrowserChannel channel, string? reason, CancellationToken ct)
    {
        if (_endAnnounced || _closing || !_started) return;

        switch (reason)
        {
            case "expired":
                await EndNoticeAsync(channel, "max_duration", ct);
                break;
            case "content":
                _logger.LogWarning("LiveVoice: oturum güvenlik filtresince kapatıldı");
                await EndNoticeAsync(channel, "content_policy", ct);
                break;
            default:
                await SendAsync(channel, new { type = "error", message = "Bağlantı koptu." }, ct);
                break;
        }
    }

    private async Task EndNoticeAsync(IBrowserChannel channel, string reason, CancellationToken ct)
    {
        _endAnnounced = true;
        await SendAsync(channel, new { type = "conversation_ended", reason }, ct);
    }

    /// <summary>Bağlantılar kapandıktan sonra: bekleyen turlar ve son kullanım yazılır. Bağlantı durumundan bağımsız tamamlanmalı.</summary>
    private async Task FinishAsync(IBrowserChannel channel, AgentSession session)
    {
        var ct = CancellationToken.None;
        await ApplyAsync(channel, session, _assembler.DrainAll(), ct);

        await BillFinalVoiceAsync(session.SessionId);
        var voiceCost = VoiceCost(_voiceSeconds);
        var at = _clock.GetUtcNow().UtcDateTime;
        if (_usage is not null)
        {
            try
            {
                if (_voiceSeconds > 0)
                {
                    await _usage.RecordAsync(new LlmCallRecord(
                        _client.ModelName, Provider, 0, 0, voiceCost, _voiceSeconds * 1000, at, session.SessionId), ct);
                }
                foreach (var call in _backendCalls) await _usage.RecordAsync(call, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "LiveVoice: kullanım kaydı başarısız session={Sid}", session.SessionId); }
        }

        _logger.LogInformation(
            "[Usage] oturum={Session} ses {Seconds:F0} sn (${VoiceCost:F4}) | arka plan in {Input}/out {Output} ({Calls} çağrı, ${BackendCost:F4})",
            session.SessionId.Length <= 8 ? session.SessionId : session.SessionId[..8], _voiceSeconds, voiceCost,
            _backendCalls.Sum(c => c.InputTokens), _backendCalls.Sum(c => c.OutputTokens), _backendCalls.Count,
            _backendCalls.Sum(c => c.CostUsd));
    }

    // Tarayıcı gitmiş olabilir: görüşmenin son işleri (kullanım, kayıt) bu yüzden kesilmemeli.
    private static async Task SendAsync(IBrowserChannel channel, object payload, CancellationToken ct)
    {
        try { await channel.SendJsonAsync(payload, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* tarayıcı kapanmış */ }
    }

    private static async Task SendBinaryAsync(IBrowserChannel channel, byte[] audio, CancellationToken ct)
    {
        try { await channel.SendBinaryAsync(audio, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* tarayıcı kapanmış */ }
    }
}
