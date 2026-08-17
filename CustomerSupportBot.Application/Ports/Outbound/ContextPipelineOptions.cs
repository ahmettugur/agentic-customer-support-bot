namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Bağlam kurulumunun sınırları. Bağlam bir <b>iyileştirmedir, zorunluluk değildir</b>:
/// üretimi turu süresiz bekletmemeli ve prompt'u sınırsız şişirmemelidir.
/// </summary>
public class ContextPipelineOptions
{
    /// <summary>
    /// Tek bir provider'a tanınan süre. Aşılırsa o provider atlanır, tur devam eder.
    ///
    /// <para>
    /// Neden gerekli: pipeline içinde ağ ve LLM çağrıları var
    /// (<c>SemanticMemoryContextProvider</c> embedding + vektör araması,
    /// <c>ConversationSummaryProvider</c> doğrudan bir <c>IChatClient.CompleteAsync</c>).
    /// Bağlam kurulumu workflow'dan ÖNCE çalıştığı için <c>WorkflowGuardOptions.TimeoutSeconds</c>
    /// koruması burada henüz devrede değildir — bu ayar olmadan yavaş bir provider turu
    /// belirsiz süre bloklar.
    /// </para>
    /// </summary>
    public int ProviderTimeoutSeconds { get; set; } = 5;

    /// <summary>Tek bir provider'ın katkısı için üst sınır (karakter).</summary>
    public int MaxProviderChars { get; set; } = 4000;

    /// <summary>
    /// Tüm bağlamın toplam üst sınırı (karakter). Tavana ulaşıldığında <b>düşük öncelikli</b>
    /// (yüksek <c>Order</c>) provider'lar dışarıda bırakılır — kritik olanlar önce yerleşir.
    /// </summary>
    public int MaxTotalChars { get; set; } = 12000;
}
