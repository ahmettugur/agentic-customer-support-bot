// Agents/SubTaskOrchestrator.cs
// Compound query decomposition orkestrasyonu.
// Reasoning modelinin 2+ farklı specialist'e yönelen alt görev üretmesi durumunda,
// her biri sırayla ayrı bir workflow run olarak yürütülür ve sonuçlar birleştirilir.

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using CustomerSupportBot.Models;

namespace CustomerSupportBot.Agents;

/// <summary>
/// Compound query (bileşik sorgu) ayrıştırma orkestratörü.
/// Reasoning sonucundaki SubTasks listesine göre her alt görevi
/// sırayla ayrı bir workflow run olarak yürütür.
/// </summary>
public class SubTaskOrchestrator
{
    /// <summary>
    /// Reasoning result'ın compound query olduğuna karar verir.
    /// En az 2 farklı TargetAgent olan 2+ subtask varsa decompose.
    /// </summary>
    public static bool IsCompoundQuery(ReasoningResult? r)
    {
        if (r == null || r.SubTasks.Count < 2) return false;

        var distinctAgents = r.SubTasks
            .Select(s => s.TargetAgent)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return distinctAgents >= 2;
    }

    /// <summary>
    /// Bir subtask için downstream çağrılara iletilecek mini reasoning result üretir.
    /// SubTasks listesi boş bırakılır — recursive çağrı sonsuz döngüye girmez.
    /// </summary>
    public static ReasoningResult CreateSubTaskReasoning(ReasoningResult parent, SubTask subTask)
    {
        var nextAction = string.IsNullOrWhiteSpace(subTask.TargetAgent)
            ? subTask.Description
            : $"{subTask.TargetAgent} ile alt görevi yürüt: {subTask.Description}";

        return new ReasoningResult
        {
            Analysis = $"Compound query'nin alt görevi {subTask.Order}/{parent.SubTasks.Count}: {subTask.Description}",
            Intent = string.IsNullOrWhiteSpace(subTask.Intent) ? parent.Intent : subTask.Intent,
            Rationale = parent.Rationale,
            NextAction = nextAction,
            DecisionReason = "Parent reasoning'in decompose kararına dayanılarak türetildi.",
            Confidence = WellKnown.Confidence.High,
            ConfidenceScore = 0.85,
            RequiredInfo = new List<string>(),
            Assumptions = new List<string>(),
            Steps = new List<ReasoningStep>(),
            SanityIssues = new List<ReasoningIssue>(),
            SubTasks = new List<SubTask>() // ← Recursive loop önleyici
        };
    }

    /// <summary>
    /// Subtask için downstream workflow'a iletilecek kullanıcı mesajını formatlar.
    /// </summary>
    public static string FormatSubTaskQuery(SubTask subTask)
    {
        var sb = new StringBuilder();
        sb.Append(string.IsNullOrWhiteSpace(subTask.Description)
            ? (subTask.Intent ?? "alt görev")
            : subTask.Description);

        if (subTask.Entities.Count > 0)
        {
            sb.Append(" (");
            sb.Append(string.Join(", ",
                subTask.Entities.Select(kv => $"{kv.Key}={kv.Value}")));
            sb.Append(')');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Aggregated response içinde her subtask sonucunu başlık + içerik olarak sunar.
    /// </summary>
    public static string FormatSubTaskResult(SubTask subTask, string subResponse)
    {
        var clean = (subResponse ?? string.Empty).Trim();
        var header = $"**{subTask.Order}) {subTask.Description}**";
        return $"{header}\n\n{clean}";
    }

    /// <summary>
    /// Alt görev sonuçlarını tek yanıtta birleştirir.
    /// </summary>
    public static string AggregateSubTaskResults(IReadOnlyList<string> parts)
    {
        return string.Join("\n\n---\n\n", parts);
    }
}
