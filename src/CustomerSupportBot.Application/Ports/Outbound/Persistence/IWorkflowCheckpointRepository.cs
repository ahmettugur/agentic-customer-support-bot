// Ports/Outbound/Persistence/IWorkflowCheckpointRepository.cs
// Workflow checkpoint'lerinin kalıcı deposu. Bir "tur" (turn) bir workflow koşusudur ve trace kimliğiyle anılır.
// MAF'ın checkpoint deposu (RepositoryCheckpointStore) ve çökme sonrası devam servisi bunu kullanır.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>
/// Bir turun yeniden başlatılabilmesi için checkpoint'ten ayrıca gereken bağlam. Checkpoint workflow durumunu
/// taşır; sorgu, müşteri ve reasoning sonucu ise yalnızca bellekteki trace'te durur ve çökmede kaybolur.
/// </summary>
/// <param name="HistoryCount">
/// Tur başladığında oturum geçmişindeki mesaj sayısı. Devam etmeden önce geçmiş bu sayıdan büyüdüyse kullanıcı
/// ilerlemiştir (yeniden denemiş, temsilci devralmış olabilir) ve eski tur devam ettirilmez.
/// </param>
public sealed record WorkflowTurnContext(
    string TraceId,
    string SessionId,
    string Query,
    string? CustomerId,
    ReasoningResult? Reasoning,
    int HistoryCount,
    DateTime StartedAtUtc);

public sealed record WorkflowCheckpointEntry(string CheckpointId, string? ParentCheckpointId, DateTime CreatedAtUtc);

/// <summary>Son etkinliği (son checkpoint ya da tur başlangıcı) kesim zamanından eski olan tur.</summary>
public sealed record WorkflowIdleTurn(string TraceId, DateTime LastActivityUtc);

public interface IWorkflowCheckpointRepository
{
    /// <summary>Turu kaydeder (aynı trace için tekrar çağrı bağlamı günceller).</summary>
    Task BeginTurnAsync(WorkflowTurnContext turn, CancellationToken ct = default);

    Task<WorkflowTurnContext?> GetTurnAsync(string traceId, CancellationToken ct = default);

    Task AddCheckpointAsync(string traceId, string checkpointId, string? parentCheckpointId, string payloadJson,
        CancellationToken ct = default);

    /// <summary>Checkpoint yükü (MAF'ın serileştirdiği JSON); yoksa <c>null</c>.</summary>
    Task<string?> GetCheckpointPayloadAsync(string traceId, string checkpointId, CancellationToken ct = default);

    /// <summary>Turun checkpoint'leri, eskiden yeniye.</summary>
    Task<IReadOnlyList<WorkflowCheckpointEntry>> ListCheckpointsAsync(string traceId, CancellationToken ct = default);

    /// <summary>
    /// Son etkinliği <paramref name="cutoffUtc"/>'den eski turlar (en eskiden yeniye, en fazla <paramref name="max"/>).
    /// Yaşayan bir pod'daki tur her checkpoint'te ilerler; bu yüzden kesim süresi turun zaman aşımından uzun
    /// seçilirse yalnızca sahibi ölmüş turlar döner.
    /// </summary>
    Task<IReadOnlyList<WorkflowIdleTurn>> ListTurnsIdleSinceAsync(DateTime cutoffUtc, int max, CancellationToken ct = default);

    /// <summary>Turu ve tüm checkpoint'lerini siler. Olmayan tur hata değildir.</summary>
    Task DeleteTurnAsync(string traceId, CancellationToken ct = default);
}
