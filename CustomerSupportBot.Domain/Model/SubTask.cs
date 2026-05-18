// Models/SubTask.cs
// Sub-task decomposition.
// Compound query'lerde (ör. "ORD-1 nerede ve ORD-2 için şikayet açmak istiyorum")
// Reasoning modelinin query'yi birden fazla alt göreve ayırmasını sağlar. Her alt
// Görev kendi intent + targetAgent + entities ile yapılandırılmış olarak saklanır.
//
// Şu an workflow mevcut akışı koruyor (tek planning + tek agent seçimi), ancak
// Planning prompt'u subTasks'ı görür ve çoklu agent yönlendirmesi yapabilir.
// İleride (Phase 4b) workflow iteration eklenebilir — her subtask için ayrı run.

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
    /// Bu alt görevin ihtiyacı olan entity'ler (ör. { "order_id": "ORD-1" }).
    /// Reasoning tarafı VerifiedEntities'ten türetir.
    /// </summary>
    public Dictionary<string, string> Entities { get; set; } = new();

    /// <summary>
    /// Bu alt görevin öncesinde tamamlanması gereken diğer sub-task sıraları
    /// (ör. [1] = önce 1. subtask bitmeli). Boş = bağımsız.
    /// </summary>
    public List<int> Dependencies { get; set; } = new();
}

