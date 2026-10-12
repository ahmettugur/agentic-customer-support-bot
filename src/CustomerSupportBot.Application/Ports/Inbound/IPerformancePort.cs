// Ports/Inbound/IPerformancePort.cs
// Performans ekranı: yapay zekâ ajanları, araçlar ve temsilciler — seçilen tarih aralığında.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <param name="Traces">Asistanın işlediği tur (trace) sayısı.</param>
/// <param name="AnsweredRate">Asistanın kendi başına yanıtladığı turların oranı (0–1).</param>
/// <param name="ErrorRate">Hatayla biten turların oranı.</param>
/// <param name="EscalationRate">Temsilciye aktarımla biten turların oranı.</param>
/// <param name="ToolFailureRate">Başarısız araç çağrılarının oranı; hiç çağrı yoksa null.</param>
public sealed record AiSummary(
    int Traces,
    double AnsweredRate,
    double ErrorRate,
    double EscalationRate,
    double AvgDurationMs,
    int ToolCalls,
    double? ToolFailureRate);

/// <param name="Outcomes">Uzmanın sonuç bildirdiği görev sayısı (planlama/yanıt ajanlarında 0).</param>
/// <param name="SuccessRate">(Tamamlanan + onaya gönderilen) / sonuç; sonuç yoksa null.</param>
public sealed record AiAgentRow(
    string AgentName,
    int Turns,
    double AvgDurationMs,
    int Outcomes,
    int Succeeded,
    int Escalated,
    int NeedsFollowUp,
    int Failed,
    double? SuccessRate);

public sealed record ToolRow(string ToolName, string? AgentName, int Calls, int Failures, double FailureRate, string? LastFailure);

public sealed record HumanAgentRow(
    string AgentId,
    string DisplayName,
    int EscalationsTaken,
    int EscalationsResolved,
    double? AvgFirstResponseSeconds,
    double? AvgResolutionSeconds,
    int ComplaintsClosed,
    double? AvgRating,
    int Ratings);

public sealed record PerformanceReport(
    DateTime FromUtc,
    DateTime ToUtc,
    AiSummary Summary,
    IReadOnlyList<AiAgentRow> Agents,
    IReadOnlyList<ToolRow> Tools,
    IReadOnlyList<HumanAgentRow> HumanAgents,
    IReadOnlyList<TerminationCount> Terminations);

public interface IPerformancePort
{
    /// <summary>
    /// Varsayılan aralık son 7 gün; en fazla 92 gün. Geçersiz aralıkta <see cref="ArgumentException"/>.
    /// </summary>
    Task<PerformanceReport> GetAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
