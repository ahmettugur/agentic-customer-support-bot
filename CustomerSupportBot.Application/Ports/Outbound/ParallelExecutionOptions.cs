// Application/Services/ParallelExecutionOptions.cs
// Compound query alt görevleri için paralel çalıştırma ayarları.
//
// Yan-etkisizlik ajan bazında değil, TOOL bazında bir özelliktir: ProductAgent'ın tüm
// tool'ları salt-okunur olduğu için ajan bazlı kısayol (WellKnown.AgentNames.ReadOnly)
// onun için yeterli. Ama OrderAgent HEM salt-okunur (order_status_tool, get_last_order_tool,
// get_all_orders_tool) HEM yan-etkili (order_placement_tool, order_cancel_tool,
// return_request_tool) tool'lara sahip — bu yüzden "OrderAgent = her zaman seri" kuralı
// "1042 ve 1043 siparişinin durumu ne?" gibi çok yaygın çift-sorgulama senaryolarını
// gereksiz yere serileştiriyordu. Planning aşamasında her subtask için üretilen `Intent`
// alanı, hangi tool grubunun tetikleneceğinin güvenilir bir vekilidir (LLM subtask'ı
// "sipariş_sorgulama" olarak sınıflandırdıysa order_status_tool/get_last_order_tool
// dışında bir şey çağırmaz) — bu yüzden Order için ek olarak intent bazlı incelik uygulanır.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Bileşik (compound) sorgudaki alt görevlerin paralel yürütme politikası.
/// </summary>
public class ParallelExecutionOptions
{
    public const string SectionName = "ParallelExecution";

    /// <summary>Paralel sub-task çalıştırma aktif mi?</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Aynı anda en fazla kaç yan-etkisiz alt görev çalıştırılabilir.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 4;

    /// <summary>OrderAgent'ın salt-okunur tool'larına karşılık gelen intent'ler.</summary>
    private static readonly HashSet<string> ReadOnlyOrderIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        WellKnown.Intents.OrderInquiry,
        WellKnown.Intents.OrderListing
    };

    /// <summary>
    /// Bir alt görevin yan-etkisiz olarak değerlendirilip paralel batch'e alınabileceğini
    /// belirler. ProductAgent gibi tamamen salt-okunur ajanlar için
    /// <see cref="WellKnown.AgentNames.ReadOnly"/> yeterli; OrderAgent gibi karma
    /// (hem okuma hem yazma tool'u olan) ajanlar için ek olarak subtask'ın Intent'ine bakılır.
    /// </summary>
    public bool IsReadOnly(SubTask sub)
    {
        if (string.IsNullOrWhiteSpace(sub.TargetAgent)) return false;

        if (WellKnown.AgentNames.ReadOnly.Contains(sub.TargetAgent)) return true;

        if (string.Equals(sub.TargetAgent, WellKnown.AgentNames.Order, StringComparison.OrdinalIgnoreCase))
            return ReadOnlyOrderIntents.Contains(sub.Intent);

        return false;
    }
}
