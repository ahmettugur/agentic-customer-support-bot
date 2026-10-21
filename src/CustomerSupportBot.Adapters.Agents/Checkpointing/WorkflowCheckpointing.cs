// Adapters.Agents/Checkpointing/WorkflowCheckpointing.cs
// Workflow checkpoint'lemenin tek giriş noktası: açık/kapalı kararı, MAF CheckpointManager'ı ve bir turun yaşam döngüsü
// (başlat → adım adım checkpoint → bitince temizle ya da kapanış yüzünden kesildiyse sakla).

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents.Checkpointing;

public sealed class WorkflowCheckpointing
{
    private readonly IWorkflowCheckpointRepository _repository;
    private readonly IOptions<WorkflowCheckpointingOptions> _options;
    private readonly ILogger<WorkflowCheckpointing> _logger;
    private readonly IShutdownSignal? _shutdown;

    public WorkflowCheckpointing(
        IWorkflowCheckpointRepository repository,
        IOptions<WorkflowCheckpointingOptions> options,
        ILogger<WorkflowCheckpointing> logger,
        IShutdownSignal? shutdown = null)
    {
        _repository = repository;
        _options = options;
        _logger = logger;
        _shutdown = shutdown;
        Manager = CheckpointManager.CreateJson(new RepositoryCheckpointStore(repository));
    }

    public bool Enabled => _options.Value.Enabled;

    /// <summary>MAF checkpoint yöneticisi: durumu kalıcı repository'ye yazar, oradan okur.</summary>
    public CheckpointManager Manager { get; }

    /// <summary>
    /// Yalnızca gerçek sohbet turları checkpoint'lenir: devam ettirilen turun yanıtı oturum geçmişine yazılacağından
    /// oturum ve geçmiş şarttır (eval/A2A gibi oturumsuz koşular dışarıda). Alt görev koşuları da dışarıda: bileşik
    /// turun diğer alt görevleri ve birleştirmesi yeniden kurulamaz.
    /// </summary>
    public bool ShouldCheckpoint(AgentSession? session, List<ConversationMessage>? history, ReasoningResult? reasoning) =>
        Enabled && session is not null && history is not null && reasoning?.ConstrainedTargetAgent is null;

    /// <summary>
    /// Turu kaydeder. Checkpoint'leme bir güvenlik ağıdır: kaydedilemezse tur checkpoint'siz devam eder
    /// (<c>false</c> döner), müşterinin yanıtı bundan etkilenmez.
    /// </summary>
    public async Task<bool> TryBeginAsync(ReasoningTrace trace, string query, AgentSession session, int historyCount, ReasoningResult? reasoning)
    {
        try
        {
            await _repository.BeginTurnAsync(new WorkflowTurnContext(
                trace.TraceId, session.SessionId, query, session.State.AuthenticatedCustomerId, reasoning, historyCount, DateTime.UtcNow));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Checkpoint] Tur kaydedilemedi, checkpoint'siz devam ediliyor (trace={TraceId})", trace.TraceId);
            return false;
        }
    }

    /// <summary>
    /// Tur biterken çağrılır. Normal bitiş, hata, zaman aşımı ve istemcinin vazgeçmesi checkpoint'leri SİLER; yalnızca
    /// host kapanırken iptal edilen tur (rolling update) saklanır ki yeni pod kaldığı yerden bitirsin. Çökmede bu
    /// metot hiç çalışmaz — checkpoint'ler zaten kalır.
    /// </summary>
    public async Task EndAsync(string traceId, CancellationToken turnToken)
    {
        if (turnToken.IsCancellationRequested && _shutdown is { IsStopping: true })
        {
            _logger.LogInformation("[Checkpoint] Tur kapanış sırasında kesildi, devam için saklanıyor (trace={TraceId})", traceId);
            return;
        }

        try
        {
            // Temizlik, iptal edilmiş istek token'ına bağlı OLMAMALI: istemci vazgeçince de silinmeli.
            await _repository.DeleteTurnAsync(traceId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Checkpoint] Tur temizlenemedi; süre dolunca devam taraması temizler (trace={TraceId})", traceId);
        }
    }
}
