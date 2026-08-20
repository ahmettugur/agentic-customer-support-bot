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
    private static readonly HashSet<string> AllowedEntityKeys = new(StringComparer.Ordinal)
    {
        "order_id", "customer_id", "complaint_id"
    };

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
            ConstrainedTargetAgent = subTask.TargetAgent,
            // subTask.Entities yürütme başlamadan önce ValidateExecutionPlan tarafından kaynak
            // sorgu/parent verified entity ile bağlanmıştır. Burada VerifiedEntities'e çevrilmezse
            // WorkflowRunner.ResolveExtractedIds
            // fallback'e düşüp FormatSubTaskQuery'nin "açıklama (order_id=1042)" biçimindeki
            // sentetik metnini IdExtractor.Extract ile regex'ten geçirmeye çalışır — bu metinde
            // "sipariş"/"müşteri" gibi Türkçe bağlam kelimeleri YOK, dolayısıyla o regex çoğu
            // zaman hiçbir şey bulamaz veya yanlış sınıflandırır.
            VerifiedEntities = BuildVerifiedEntities(subTask.Entities)
        };
    }

    /// <summary>
    /// LLM tarafından üretilen alt görev planını workflow fan-out başlamadan önce doğrular.
    /// Bağımlılıklar yalnızca daha önceki görevlere işaret edebilir; eksik kenarlar, ileri
    /// bağımlılıklar ve döngüler yürütme başlamadan fail-closed davranır.
    /// </summary>
    public static List<SubTask> ValidateExecutionPlan(
        ReasoningResult reasoning,
        string originalQuery,
        ParallelExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(reasoning);

        var ordered = reasoning.SubTasks.OrderBy(s => s.Order).ToList();
        if (ordered.Count == 0)
            throw new InvalidOperationException("Alt görev planı boş olamaz.");
        if (ordered.Count > options.MaxSubTasks)
            throw new InvalidOperationException(
                $"Compound plan {ordered.Count} alt görev içeriyor; izin verilen üst sınır {options.MaxSubTasks}.");

        var orders = ordered.Select(s => s.Order).ToHashSet();
        if (orders.Count != ordered.Count || ordered.Any(s => s.Order <= 0))
            throw new InvalidOperationException("Alt görev sıraları pozitif ve benzersiz olmalıdır.");

        foreach (var sub in ordered)
        {
            if (string.IsNullOrWhiteSpace(sub.Description))
                throw new InvalidOperationException($"Alt görev {sub.Order} açıklama içermiyor.");
            if (!WellKnown.AgentNames.Specialists.Contains(
                    sub.TargetAgent, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Alt görev {sub.Order} bilinmeyen specialist hedefliyor: '{sub.TargetAgent}'.");

            if (sub.Dependencies.Count != sub.Dependencies.Distinct().Count())
                throw new InvalidOperationException($"Alt görev {sub.Order} yinelenen bağımlılık içeriyor.");
            if (sub.Dependencies.Any(dependency => !orders.Contains(dependency)))
                throw new InvalidOperationException($"Alt görev {sub.Order} mevcut olmayan bir göreve bağımlı.");
            if (sub.Dependencies.Any(dependency => dependency >= sub.Order))
                throw new InvalidOperationException(
                    $"Alt görev {sub.Order} yalnızca kendisinden önceki görevlere bağımlı olabilir.");

            ValidateEntities(sub, reasoning.VerifiedEntities, originalQuery);
        }

        return ordered;
    }

    private static void ValidateEntities(
        SubTask sub,
        VerifiedEntities? parentEntities,
        string originalQuery)
    {
        foreach (var (key, value) in sub.Entities)
        {
            if (!AllowedEntityKeys.Contains(key))
                throw new InvalidOperationException(
                    $"Alt görev {sub.Order} desteklenmeyen entity içeriyor: '{key}'.");
            if (string.IsNullOrWhiteSpace(value) || !value.All(char.IsAsciiDigit))
                throw new InvalidOperationException(
                    $"Alt görev {sub.Order} için '{key}' geçerli sayısal formatta değil.");
            if (!ContainsNumericToken(sub.Description, value))
                throw new InvalidOperationException(
                    $"Alt görev {sub.Order} için '{key}' değeri görev açıklamasına bağlı değil.");

            var appearsInQuery = ContainsNumericToken(originalQuery, value);
            var appearsInVerifiedParent = ParentContains(parentEntities, key, value);
            if (!appearsInQuery && !appearsInVerifiedParent)
                throw new InvalidOperationException(
                    $"Alt görev {sub.Order} için '{key}' değeri kullanıcı girdisinden doğrulanamadı.");
        }
    }

    private static bool ParentContains(VerifiedEntities? parent, string key, string value) => key switch
    {
        "order_id" => string.Equals(parent?.OrderId?.Value, value, StringComparison.Ordinal),
        "customer_id" => string.Equals(parent?.CustomerId?.Value, value, StringComparison.Ordinal),
        "complaint_id" => string.Equals(parent?.ComplaintId?.Value, value, StringComparison.Ordinal),
        _ => false
    };

    private static bool ContainsNumericToken(string text, string value)
    {
        var start = 0;
        while (start <= text.Length - value.Length)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0) return false;

            var leftIsDigit = index > 0 && char.IsAsciiDigit(text[index - 1]);
            var rightIndex = index + value.Length;
            var rightIsDigit = rightIndex < text.Length && char.IsAsciiDigit(text[rightIndex]);
            if (!leftIsDigit && !rightIsDigit) return true;

            start = index + 1;
        }
        return false;
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
    /// <summary>
    /// Alt görev sonucunun başlığı (gövdeden önceki kısım, boş satır dahil).
    ///
    /// <para>
    /// Ayrı bir metot olması şart: <c>DecomposedRunner</c> sıralı alt görevlerde gerçek token
    /// akışını canlı iletirken başlığı gövdeden ÖNCE yayınlamak zorunda — gövde henüz üretilmemiş
    /// olduğu için <see cref="FormatSubTaskResult"/> o anda çağrılamaz. Başlık iki yerde ayrı
    /// yazılsaydı akan metin ile nihai metin sessizce ayrışırdı.
    /// </para>
    /// </summary>
    public static string FormatSubTaskHeader(SubTask subTask)
        => $"**{subTask.Order}) {subTask.Description}**\n\n";

    public static string FormatSubTaskResult(SubTask subTask, string subResponse)
        => FormatSubTaskHeader(subTask) + (subResponse ?? string.Empty).Trim();

    /// <summary>
    /// Alt görev sonuçlarını tek yanıtta birleştirir.
    /// </summary>
    /// <summary>
    /// Alt görev sonuçlarını ayıran metin. <b>Sabit olması şart:</b> <c>DecomposedRunner</c>
    /// alt görev sonuçlarını kanonik sırada tek tek <c>response_delta</c> olarak yayınlar ve
    /// aralarına bu ayırıcıyı koyar; yayınlanan parçaların birleşimi
    /// <see cref="AggregateSubTaskResults"/> çıktısına birebir eşit olmalıdır. Ayırıcı iki
    /// yerde ayrı ayrı yazılsaydı biri değiştiğinde ekranda akan metin ile nihai metin
    /// sessizce birbirinden ayrılırdı.
    /// </summary>
    public const string ResultSeparator = "\n\n---\n\n";

    public static string AggregateSubTaskResults(IReadOnlyList<string> parts)
    {
        return string.Join(ResultSeparator, parts);
    }

    /// <summary>
    /// Sıralı subtask listesini yan-etkisiz (paralel) ve yan-etkili (serial)
    /// gruplara ayırır. Order alanına göre sırayla iterasyon yapılır; aynı türde
    /// ardı ardına gelen subtask'lar tek bir grupta toplanır. Bu sayede sıralama
    /// (örn. read → write → read) korunmuş olur.
    ///
    /// <para>
    /// <b>Bağımlılık kuralı:</b> <see cref="SubTask.Dependencies"/> ile bildirilen öncüller
    /// asla aynı paralel batch'e alınmaz. DecomposedRunner paralel bir grubun tüm elemanlarını
    /// <b>aynı history snapshot'ıyla</b> eşzamanlı başlatır (bkz. <c>historySnapshot</c>) —
    /// yani aynı batch'teki bir kardeşin sonucu diğerine görünmez. Gruplar birbirini
    /// <c>Task.WhenAll</c> ile beklediğinden, öncülü <b>önceki</b> bir gruba düşürmek
    /// bağımlılığı karşılamak için yeterlidir.
    /// </para>
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
        var batchOrders = new HashSet<int> { ordered[0].Order };

        for (var i = 1; i < ordered.Count; i++)
        {
            var sub = ordered[i];
            var canParallel = options.Enabled && options.IsReadOnly(sub);

            // Seri gruplar zaten Order sırasıyla tek tek yürütülür — orada bağımlılık
            // kendiliğinden karşılanır. Kontrol yalnızca paralel batch için anlamlı.
            var dependsOnBatchMember = currentParallel
                && sub.Dependencies.Count > 0
                && sub.Dependencies.Any(batchOrders.Contains);

            if (canParallel == currentParallel && !dependsOnBatchMember)
            {
                currentBatch.Add(sub);
                batchOrders.Add(sub.Order);
            }
            else
            {
                groups.Add(new SubTaskGroup(currentParallel, currentBatch));
                currentBatch = new List<SubTask> { sub };
                batchOrders = new HashSet<int> { sub.Order };
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
