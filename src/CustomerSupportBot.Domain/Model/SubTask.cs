// Models/SubTask.cs
// Sub-task decomposition.
// Compound query'lerde (ör. "1030 siparişi nerede ve 1042 için şikayet açmak istiyorum")
// Reasoning modelinin query'yi birden fazla alt göreve ayırmasını sağlar. Her alt
// Görev kendi intent + targetAgent + entities ile yapılandırılmış olarak saklanır.
//
// DecomposedRunner her alt görevi ayrı ve hedef specialist'e kısıtlı bir workflow koşusunda
// yürütür; Dependencies yalnızca açıkça bağımlı alt görevlerin sonuçlarını bağlama ekler.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Compound query'nin parçası olan bir alt görev.
/// SubTasks listesi 0 veya 1 elemanlı ise query single-intent; 2+ ise decomposition var.
/// </summary>
public class SubTask
{
    /// <summary>1-indexed yürütme sırası.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Bu alt görevin niyeti (ör. "sipariş_sorgulama", "şikayet").
    /// Üst düzey <see cref="ReasoningResult.Intent"/>'ten farklı olabilir.
    /// </summary>
    public string Intent { get; set; } = "";

    /// <summary>
    /// İnsanlara yönelik kısa açıklama (UI'da ve planning hint'inde kullanılır).
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Bu alt görevi yürütecek specialist agent adı
    /// (ör. "OrderAgent", "ComplaintAgent").
    /// </summary>
    public string TargetAgent { get; set; } = "";

    /// <summary>
    /// Bu alt görevin ihtiyacı olan entity'ler (ör. { "order_id": "1030" }).
    /// LLM tarafından üretilir; yürütmeden önce SubTaskOrchestrator tarafından biçim ve
    /// kaynak sorgu/parent verified entity ile bağlanma açısından doğrulanır.
    /// </summary>
    public Dictionary<string, string> Entities { get; set; } = new();

    /// <summary>
    /// Bu alt görevin öncesinde tamamlanması gereken diğer sub-task sıraları
    /// (ör. [1] = önce 1. subtask bitmeli). Boş = bağımsız.
    /// <c>SubTaskOrchestrator.Partition</c> bunu okur: bildirilen öncül aynı paralel
    /// batch'e düşemez, batch orada kapatılır (aynı batch'teki kardeşler eşzamanlı
    /// başlar ve birbirinin sonucunu göremez).
    /// </summary>
    public List<int> Dependencies { get; set; } = new();
}
