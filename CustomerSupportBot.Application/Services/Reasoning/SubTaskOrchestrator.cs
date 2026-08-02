// Application/Services/SubTaskOrchestrator.cs
// Compound query decomposition orkestrasyonu.
// Reasoning modelinin 2+ farklı specialist'e yönelen alt görev üretmesi durumunda,
// her biri sırayla ayrı bir workflow run olarak yürütülür ve sonuçlar birleştirilir.

using System.Text;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Reasoning;

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
            SubTasks = new List<SubTask>(), // ← Recursive loop önleyici
            // subTask.Entities zaten VerifiedEntities'ten türetilmişti (bkz. SubTask.Entities
            // yorumu) — burada VerifiedEntities'e GERİ çevrilmezse WorkflowRunner.ResolveExtractedIds
            // fallback'e düşüp FormatSubTaskQuery'nin "açıklama (order_id=1042)" biçimindeki
            // sentetik metnini IdExtractor.Extract ile regex'ten geçirmeye çalışır — bu metinde
            // "sipariş"/"müşteri" gibi Türkçe bağlam kelimeleri YOK, dolayısıyla o regex çoğu
            // zaman hiçbir şey bulamaz veya yanlış sınıflandırır.
            VerifiedEntities = BuildVerifiedEntities(subTask.Entities)
        };
    }

    /// <summary>
    /// SubTask.Entities (düz "order_id"/"customer_id"/"complaint_id" → değer sözlüğü) üzerinden
    /// bir VerifiedEntities kurar. Orijinal DB-doğrulama durumu decompose sırasında sözlüğe
    /// düzleştirilirken kaybolduğu için <see cref="EntityVerification.FormatOnly"/> kullanılır —
    /// WorkflowRunner.ResolveExtractedIds yalnızca HasAny/değer bakar, verification seviyesine
    /// duyarlı değil.
    /// </summary>
    private static VerifiedEntities? BuildVerifiedEntities(Dictionary<string, string> entities)
    {
        if (entities.Count == 0) return null;

        var result = new VerifiedEntities();
        if (entities.TryGetValue("order_id", out var orderId) && !string.IsNullOrWhiteSpace(orderId))
            result.OrderId = new VerifiedEntity { Value = orderId, Source = EntitySource.Derived, Verification = EntityVerification.FormatOnly };
        if (entities.TryGetValue("customer_id", out var customerId) && !string.IsNullOrWhiteSpace(customerId))
            result.CustomerId = new VerifiedEntity { Value = customerId, Source = EntitySource.Derived, Verification = EntityVerification.FormatOnly };
        if (entities.TryGetValue("complaint_id", out var complaintId) && !string.IsNullOrWhiteSpace(complaintId))
            result.ComplaintId = new VerifiedEntity { Value = complaintId, Source = EntitySource.Derived, Verification = EntityVerification.FormatOnly };

        return result.HasAny ? result : null;
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

    /// <summary>
    /// Sıralı subtask listesini yan-etkisiz (paralel) ve yan-etkili (serial)
    /// gruplara ayırır. Order alanına göre sırayla iterasyon yapılır; aynı türde
    /// ardı ardına gelen subtask'lar tek bir grupta toplanır. Bu sayede sıralama
    /// (örn. read → write → read) korunmuş olur.
    /// </summary>
    public static List<SubTaskGroup> Partition(
        IEnumerable<SubTask> subTasks,
        ParallelExecutionOptions options)
    {
        var ordered = subTasks.OrderBy(s => s.Order).ToList();
        var groups = new List<SubTaskGroup>();
        if (ordered.Count == 0) return groups;

        bool currentParallel = options.Enabled && options.IsReadOnly(ordered[0]);
        var currentBatch = new List<SubTask> { ordered[0] };

        for (var i = 1; i < ordered.Count; i++)
        {
            var sub = ordered[i];
            var canParallel = options.Enabled && options.IsReadOnly(sub);

            if (canParallel == currentParallel)
            {
                currentBatch.Add(sub);
            }
            else
            {
                groups.Add(new SubTaskGroup(currentParallel, currentBatch));
                currentBatch = new List<SubTask> { sub };
                currentParallel = canParallel;
            }
        }

        groups.Add(new SubTaskGroup(currentParallel, currentBatch));
        return groups;
    }
}

/// <summary>
/// Birlikte yürütülecek subtask grubu.
/// <see cref="Parallel"/> = true ise grup elemanları aynı anda Task.WhenAll
/// ile başlatılabilir; aksi halde sırayla yürütülmelidir.
/// </summary>
public record SubTaskGroup(bool Parallel, IReadOnlyList<SubTask> Items);
