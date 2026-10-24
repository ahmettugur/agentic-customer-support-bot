// Application/Services/Evaluation/Experiments.cs
// Oturum bazlı A/B deneyleri. Prompt'lar ajan takımı kurulurken bir kez okunduğu için oturum başına prompt seçilemez;
// bunun yerine her oturum bir kola atanır ve kolun operatör metni (Hint) tur bağlamına eklenir. Puanlar kol etiketiyle ayrışır.

using System.Security.Cryptography;
using System.Text;

namespace CustomerSupportBot.Application.Services.Evaluation;

public sealed class ExperimentOptions
{
    public const string SectionName = "Experiments";

    /// <summary>KAPALI başlar; açıkken bile yalnızca tanımlı deneyler çalışır.</summary>
    public bool Enabled { get; set; }

    // Config binder tuzağı: önceden doldurulmuş liste verilen değerleri üzerine EKLER → null varsayılan + Effective.
    public List<ExperimentDefinition>? Experiments { get; set; }
    public IReadOnlyList<ExperimentDefinition> Effective => Experiments ?? [];
}

public sealed class ExperimentDefinition
{
    public string Name { get; set; } = "";
    public List<ExperimentArm>? Arms { get; set; }
    public IReadOnlyList<ExperimentArm> EffectiveArms => Arms ?? [];
}

public sealed class ExperimentArm
{
    public string Name { get; set; } = "";

    /// <summary>Bağıl ağırlık; 0 ya da negatifse kola hiç atama yapılmaz.</summary>
    public double Weight { get; set; } = 1;

    /// <summary>Bu koldaki oturumların bağlamına eklenen metin (operatör yazar, güvenilir). Boşsa kol "kontrol"dür.</summary>
    public string? Hint { get; set; }
}

public static class ExperimentAssigner
{
    /// <summary>Sabit, ağırlıklı atama: aynı (oturum, deney) her pod'da aynı kolu verir; deneyler birbirinden bağımsızdır.</summary>
    public static ExperimentArm? Assign(string sessionId, ExperimentDefinition experiment)
    {
        var arms = experiment.EffectiveArms.Where(a => a.Weight > 0 && !string.IsNullOrWhiteSpace(a.Name)).ToList();
        if (arms.Count == 0) return null;

        var h = SHA256.HashData(Encoding.UTF8.GetBytes($"{sessionId}|{experiment.Name}"));
        var point = BitConverter.ToUInt32(h, 0) / (double)uint.MaxValue * arms.Sum(a => a.Weight);

        var cumulative = 0d;
        foreach (var arm in arms)
        {
            cumulative += arm.Weight;
            if (point < cumulative) return arm;
        }
        return arms[^1];
    }
}
