namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

/// <summary>Trace toplamları: tur sayısı, tamamlanan, hatalı, ortalama süre, eskalasyonla biten.</summary>
public sealed record TraceTotals(int Traces, int Completed, int Errors, double AvgDurationMs, int Escalated);

/// <summary>Bir ajanın çalıştığı tur sayısı ve ortalama süresi (trace'teki ajan ziyaretleri).</summary>
public sealed record AgentTurnStat(string AgentName, int Turns, double AvgDurationMs);

/// <summary>Uzman ajanın bildirdiği görev sonucu (<c>postToolReflection.status</c>) sayısı.</summary>
public sealed record AgentOutcomeStat(string AgentName, string Status, int Count);

/// <summary>Araç çağrı sayısı, başarısız olanlar ve en son hata özeti.</summary>
public sealed record ToolStat(string ToolName, string? AgentName, int Calls, int Failures, string? LastFailure);

public sealed record TerminationCount(string Reason, int Count);

public sealed record AiPerformanceData(
    TraceTotals Totals,
    IReadOnlyList<AgentTurnStat> Turns,
    IReadOnlyList<AgentOutcomeStat> Outcomes,
    IReadOnlyList<ToolStat> Tools,
    IReadOnlyList<TerminationCount> Terminations);

/// <param name="AgentId">Temsilci kimliği (<c>HumanAgent.Id</c>).</param>
/// <param name="EscalationsTaken">Dönemde açılıp bu temsilciye atanan ve üstlenilen eskalasyonlar.</param>
/// <param name="AvgFirstResponseSeconds">Eskalasyon açılışından üstlenmeye ortalama süre.</param>
/// <param name="AvgResolutionSeconds">Eskalasyon açılışından çözüme ortalama süre (yalnız çözülenler).</param>
/// <param name="ComplaintsClosed">Dönemde bu temsilciye atanmışken çözülen/reddedilen şikayetler.</param>
/// <param name="AvgRating">Temsilcinin eskalasyonlarının oturumlarına verilen müşteri puanı (1–5).</param>
public sealed record HumanAgentStat(
    string AgentId,
    int EscalationsTaken,
    int EscalationsResolved,
    double? AvgFirstResponseSeconds,
    double? AvgResolutionSeconds,
    int ComplaintsClosed,
    double? AvgRating,
    int Ratings);

/// <summary>
/// Performans ekranının toplama sorguları. Aralık <c>[fromUtc, toUtc)</c>; trace'ler başlangıç, eskalasyonlar
/// açılış, şikayet kapanışları olay zamanına göre süzülür.
/// </summary>
public interface IPerformanceStore
{
    Task<AiPerformanceData> GetAiAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<HumanAgentStat>> GetHumanAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
