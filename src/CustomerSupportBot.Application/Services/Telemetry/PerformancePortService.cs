// Application/Services/Telemetry/PerformancePortService.cs
// Performans raporu: depodaki gruplanmış satırları oranlara ve ekran satırlarına çevirir.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Telemetry;

public sealed class PerformancePortService(
    IPerformanceStore store,
    IHumanAgentRegistry agents,
    TimeProvider? clock = null) : IPerformancePort
{
    public static readonly TimeSpan DefaultRange = TimeSpan.FromDays(7);
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(92);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    // Uzmanların bildirdiği durumlar — WellKnown.TaskStatuses'taki eş anlamlılarla birlikte.
    private static readonly HashSet<string> SucceededStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnown.TaskStatuses.Done, WellKnown.TaskStatuses.PendingApproval, WellKnown.TaskStatuses.Completed,
        WellKnown.TaskStatuses.Complete, WellKnown.TaskStatuses.Success, WellKnown.TaskStatuses.Pending
    };
    private static readonly HashSet<string> EscalatedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnown.TaskStatuses.NeedsEscalation, WellKnown.TaskStatuses.Escalation, WellKnown.TaskStatuses.Escalate
    };
    private static readonly HashSet<string> FollowUpStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnown.TaskStatuses.NeedsFollowUp, WellKnown.TaskStatuses.Followup, WellKnown.TaskStatuses.NeedsFollowupNoUnderscore,
        WellKnown.TaskStatuses.Partial
    };
    private static readonly HashSet<string> FailedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnown.TaskStatuses.Failed, WellKnown.TaskStatuses.Fail, WellKnown.TaskStatuses.Error, WellKnown.TaskStatuses.Incomplete
    };

    public async Task<PerformanceReport> GetAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default)
    {
        var to = toUtc ?? _clock.GetUtcNow().UtcDateTime;
        var from = fromUtc ?? to - DefaultRange;
        if (from >= to) throw new ArgumentException("Başlangıç tarihi bitişten önce olmalı.");
        if (to - from > MaxRange) throw new ArgumentException($"En fazla {MaxRange.TotalDays:0} günlük aralık seçilebilir.");

        var ai = await store.GetAiAsync(from, to, ct);
        var human = await store.GetHumanAsync(from, to, ct);

        return new PerformanceReport(from, to, Summary(ai), AgentRows(ai), ToolRows(ai.Tools), HumanRows(human),
            ai.Terminations.OrderByDescending(t => t.Count).ToList());
    }

    private static AiSummary Summary(AiPerformanceData ai)
    {
        var t = ai.Totals;
        var calls = ai.Tools.Sum(x => x.Calls);
        return new AiSummary(
            t.Traces,
            Rate(t.Completed, t.Traces) ?? 0,
            Rate(t.Errors, t.Traces) ?? 0,
            Rate(t.Escalated, t.Traces) ?? 0,
            t.AvgDurationMs,
            calls,
            Rate(ai.Tools.Sum(x => x.Failures), calls));
    }

    private static List<AiAgentRow> AgentRows(AiPerformanceData ai)
    {
        var names = ai.Turns.Select(t => t.AgentName).Concat(ai.Outcomes.Select(o => o.AgentName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal);

        var rows = names.Select(name =>
        {
            var turn = ai.Turns.FirstOrDefault(t => t.AgentName == name);
            var outcomes = ai.Outcomes.Where(o => o.AgentName == name).ToList();
            int Count(HashSet<string> set) => outcomes.Where(o => set.Contains(o.Status)).Sum(o => o.Count);
            var total = outcomes.Sum(o => o.Count);
            var succeeded = Count(SucceededStatuses);
            return new AiAgentRow(name, turn?.Turns ?? 0, turn?.AvgDurationMs ?? 0, total, succeeded,
                Count(EscalatedStatuses), Count(FollowUpStatuses), Count(FailedStatuses), Rate(succeeded, total));
        });

        // Akıştaki sıra: planlama → uzmanlar → yanıt; tanınmayanlar sonda.
        return rows.OrderBy(r => CanonicalIndex(r.AgentName)).ThenBy(r => r.AgentName, StringComparer.Ordinal).ToList();
    }

    private static int CanonicalIndex(string agentName)
    {
        var i = Array.IndexOf(WellKnown.AgentNames.All, agentName);
        return i < 0 ? int.MaxValue : i;
    }

    private static List<ToolRow> ToolRows(IReadOnlyList<ToolStat> tools) =>
        tools.Select(t => new ToolRow(t.ToolName, t.AgentName, t.Calls, t.Failures, Rate(t.Failures, t.Calls) ?? 0, t.LastFailure))
            .OrderByDescending(t => t.Failures).ThenByDescending(t => t.Calls).ThenBy(t => t.ToolName, StringComparer.Ordinal)
            .ToList();

    private List<HumanAgentRow> HumanRows(IReadOnlyList<HumanAgentStat> stats)
    {
        var registry = agents.GetAll();
        var rows = stats.Select(s => new HumanAgentRow(s.AgentId, NameOf(registry, s.AgentId), s.EscalationsTaken, s.EscalationsResolved,
            s.AvgFirstResponseSeconds, s.AvgResolutionSeconds, s.ComplaintsClosed, s.AvgRating, s.Ratings)).ToList();

        // Hiç işi olmayan aktif temsilciler de görünür — "kimse bakmadı" da bir bilgidir.
        foreach (var a in registry.Where(a => a.IsActive && rows.All(r => r.AgentId != a.Id)))
            rows.Add(new HumanAgentRow(a.Id, a.DisplayName, 0, 0, null, null, 0, null, 0));

        return rows.OrderByDescending(r => r.EscalationsResolved + r.ComplaintsClosed)
            .ThenBy(r => r.DisplayName, StringComparer.CurrentCulture).ToList();
    }

    private static string NameOf(IReadOnlyList<HumanAgent> registry, string id) =>
        registry.FirstOrDefault(a => a.Id == id)?.DisplayName is { Length: > 0 } name ? name : id;

    private static double? Rate(int part, int total) => total > 0 ? (double)part / total : null;
}
