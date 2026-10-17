// Adapters.Agents/Checkpointing/WorkflowRecoveryService.cs
// Sahibi ölmüş (çökme, OOM, deploy) workflow turlarını bulur ve kaldıkları yerden bitirir.
//
// Sahibi öldü kararı SÜREYE dayanır: yaşayan tur her adımda checkpoint yazar ve en geç zaman aşımında biter/temizlenir.
// Son etkinliği (zaman aşımı + pay) kadar eski bir tur sahipsizdir. Başka pod'un canlı turuna dokunulmaz.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents.Checkpointing;

public sealed class WorkflowRecoveryService : IWorkflowRecoveryPort
{
    private readonly IWorkflowCheckpointRepository _repository;
    private readonly WorkflowCheckpointing _checkpointing;
    private readonly ITurnResumer _resumer;
    private readonly ISessionManager _sessions;
    private readonly WorkflowCheckpointingOptions _options;
    private readonly WorkflowGuardOptions _guards;
    private readonly ILogger<WorkflowRecoveryService> _logger;
    private readonly IAppDistributedLock? _lock;

    public WorkflowRecoveryService(
        IWorkflowCheckpointRepository repository,
        WorkflowCheckpointing checkpointing,
        ITurnResumer resumer,
        ISessionManager sessions,
        IOptions<WorkflowCheckpointingOptions> options,
        IOptions<WorkflowGuardOptions> guards,
        ILogger<WorkflowRecoveryService> logger,
        IAppDistributedLock? distributedLock = null)
    {
        _repository = repository;
        _checkpointing = checkpointing;
        _resumer = resumer;
        _sessions = sessions;
        _options = options.Value;
        _guards = guards.Value;
        _logger = logger;
        _lock = distributedLock;
    }

    public Task<WorkflowRecoveryResult> RecoverStaleTurnsAsync(CancellationToken ct = default) =>
        RecoverStaleTurnsAsync(DateTime.UtcNow, ct);

    /// <summary>Zaman dışarıdan verilebilir (testler).</summary>
    internal async Task<WorkflowRecoveryResult> RecoverStaleTurnsAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (!_checkpointing.Enabled) return new(0, 0, 0, 0);

        var cutoff = nowUtc - TimeSpan.FromSeconds(_guards.TimeoutSeconds + _options.GraceSeconds);
        // Atılan turlar devam sınırına sayılmaz; yine de tek taramada makul bir üst sınır.
        var idle = await _repository.ListTurnsIdleSinceAsync(cutoff, Math.Max(1, _options.MaxTurnsPerSweep) * 5, ct);

        int resumed = 0, discarded = 0, failed = 0, skipped = 0;
        foreach (var idleTurn in idle)
        {
            if (resumed >= _options.MaxTurnsPerSweep) break;
            ct.ThrowIfCancellationRequested();

            await using var handle = await TryClaimAsync(idleTurn.TraceId, ct);
            if (_lock is not null && handle is null) { skipped++; continue; }

            switch (await RecoverOneAsync(idleTurn.TraceId, nowUtc, ct))
            {
                case Outcome.Resumed: resumed++; break;
                case Outcome.Discarded: discarded++; break;
                case Outcome.Failed: failed++; break;
            }
        }

        if (resumed + discarded + failed + skipped > 0)
            _logger.LogInformation("[Recovery] devam={Resumed} atılan={Discarded} hatalı={Failed} atlanan={Skipped}",
                resumed, discarded, failed, skipped);
        return new(resumed, discarded, failed, skipped);
    }

    private enum Outcome { Resumed, Discarded, Failed }

    private async Task<IAsyncDisposable?> TryClaimAsync(string traceId, CancellationToken ct) =>
        _lock is null ? null : await _lock.TryAcquireAsync($"workflow-recovery:{traceId}", TimeSpan.Zero, ct);

    private async Task<Outcome> RecoverOneAsync(string traceId, DateTime nowUtc, CancellationToken ct)
    {
        var turn = await _repository.GetTurnAsync(traceId, ct);
        if (turn is null) return await DiscardAsync(traceId, "bağlam yok");

        if (nowUtc - turn.StartedAtUtc > TimeSpan.FromMinutes(_options.MaxAgeMinutes))
            return await DiscardAsync(traceId, "çok eski");

        if (turn.Reasoning?.ConstrainedTargetAgent is not null)
            return await DiscardAsync(traceId, "alt görev");

        var session = await _sessions.GetAsync(turn.SessionId, ct);
        if (session is null) return await DiscardAsync(traceId, "oturum yok");

        // Kullanıcı bu arada devam ettiyse (yeniden sordu, temsilci devraldı) eski turun yanıtı yersiz olur.
        var history = await _sessions.GetHistoryAsync(turn.SessionId, ct);
        if (history.Count != turn.HistoryCount) return await DiscardAsync(traceId, "kullanıcı ilerledi");

        var latest = await _checkpointing.Manager.GetLatestCheckpointAsync(traceId, ct);
        if (latest is null) return await DiscardAsync(traceId, "checkpoint yok");

        try
        {
            var answer = await _resumer.ResumeAsync(turn, session, latest, ct);
            await _sessions.AddExchangeAsync(turn.SessionId, turn.Query, answer, TurnSignals.From(turn.Reasoning), ct);
            await _repository.DeleteTurnAsync(traceId, CancellationToken.None);
            WorkflowMetrics.RecordRecovery("resumed");
            _logger.LogInformation("[Recovery] Tur kaldığı yerden bitirildi (trace={TraceId}, session={SessionId})", traceId, turn.SessionId);
            return Outcome.Resumed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;   // kapanış: tur saklanır, sonraki pod dener
        }
        catch (Exception ex)
        {
            // Yeniden denemek aynı hatayı tekrar üretir ve LLM parası yakar; tur bırakılır.
            _logger.LogWarning(ex, "[Recovery] Devam başarısız, tur bırakılıyor (trace={TraceId})", traceId);
            await _repository.DeleteTurnAsync(traceId, CancellationToken.None);
            WorkflowMetrics.RecordRecovery("failed");
            return Outcome.Failed;
        }
    }

    private async Task<Outcome> DiscardAsync(string traceId, string reason)
    {
        _logger.LogInformation("[Recovery] Tur devam ettirilmeden siliniyor: {Reason} (trace={TraceId})", reason, traceId);
        await _repository.DeleteTurnAsync(traceId, CancellationToken.None);
        WorkflowMetrics.RecordRecovery("discarded");
        return Outcome.Discarded;
    }
}
