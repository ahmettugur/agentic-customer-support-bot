// Models/SelfCritique.cs
// ResponseAgent'ın kendi yanıtı hakkında ürettiği kalite değerlendirmesi.
//
// Bu blok prompt'ta (Prompts/agents/response-agent.md) nihai yanıttan SONRA istenir, yani
// yanıtı düzeltmek için değil — üretildikten sonra ölçmek için vardır. Kullanıcıya asla
// gösterilmez (WorkflowResponseExtractor.RemoveTechnicalJsonBlocks temizler); trace'e yazılır
// ve LessonMiner'ın "hangi turlar incelenmeli" seçiminde sinyal olarak kullanılır.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// ResponseAgent'ın kendi yanıtına verdiği not. Tüm alanlar modelin beyanıdır —
/// doğrulanmış ölçüm değildir, bu yüzden yalnızca gözlemlenebilirlik ve iyileştirme
/// adayı seçimi için kullanılır, hiçbir çalışma zamanı kararını sürüklemez.
/// </summary>
public class SelfCritique
{
    /// <summary>Kullanıcının asıl sorusu yanıtlandı mı?</summary>
    public bool AddressesUserQuery { get; set; } = true;

    /// <summary>"appropriate" | "too_formal" | "too_casual" | "robotic" | "impolite".</summary>
    public string Tone { get; set; } = WellKnown.CritiqueTones.Appropriate;

    /// <summary>0.0–1.0; 1.0 = tam yanıt.</summary>
    public double Completeness { get; set; } = 1.0;

    /// <summary>0.0–1.0; specialist çıktısında olmayan veri/vaat üretme riski.</summary>
    public double HallucinationRisk { get; set; }

    /// <summary>Yanıtın beslendiği kaynaklar (ör. "OrderAgent.resultNotes").</summary>
    public List<string> Sources { get; set; } = new();

    /// <summary>Modelin kendi yanıtında tespit ettiği sorunlar.</summary>
    public List<string> IssuesFound { get; set; } = new();

    /// <summary>Model yanıtın düzeltilmesi gerektiğini düşünüyor mu?</summary>
    public bool RevisionNeeded { get; set; }

    /// <summary>Düzeltme önerisi (serbest metin).</summary>
    public string? RevisionNotes { get; set; }

    /// <summary>
    /// Bu turun kalite açısından incelenmeye değer olup olmadığı — LessonMiner'ın
    /// heuristik seçiminde kullanılır. Eşikler prompt'taki `revisionNeeded` kuralıyla
    /// (hallucinationRisk ≥ 0.5, completeness &lt; 0.7, tone ∈ {robotic, impolite})
    /// bilerek aynı tutulur; model bayrağı koymayı unutsa bile aynı sonuca varılır.
    /// </summary>
    public bool IsConcerning =>
        RevisionNeeded
        || !AddressesUserQuery
        || HallucinationRisk >= 0.5
        || Completeness < 0.7
        || string.Equals(Tone, WellKnown.CritiqueTones.Robotic, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Tone, WellKnown.CritiqueTones.Impolite, StringComparison.OrdinalIgnoreCase);
}
