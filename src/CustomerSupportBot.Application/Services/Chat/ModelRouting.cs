// Application/Services/Chat/ModelRouting.cs
// Model yönlendirme: basit, yüksek güvenli, yan etkisiz turlar hızlı (ucuz) modele gider; kalan her şey standart modelde kalır.

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Chat;

public sealed class ModelRoutingOptions
{
    public const string SectionName = "ModelRouting";

    /// <summary>Yönlendirme ek model kurulumu (<c>AI:OpenAI:FastModel</c>) ister; KAPALI başlar.</summary>
    public bool Enabled { get; set; }

    /// <summary>Planlama güveni en az bu kadar olmalı.</summary>
    public double MinConfidence { get; set; } = 0.75;

    // Config binder tuzağı: önceden doldurulmuş liste verilen değerleri üzerine EKLER → null varsayılan + Effective.
    public List<string>? FastIntents { get; set; }

    /// <summary>Yalnızca bilgi okuyan niyetler. Yan etkili ya da hassas olanlar (sipariş/iptal/iade/şikayet/temsilci) asla varsayılanda yoktur.</summary>
    public IReadOnlyCollection<string> EffectiveFastIntents => FastIntents ??
    [
        WellKnown.Intents.ProductInfo, WellKnown.Intents.OrderInquiry, WellKnown.Intents.OrderListing, WellKnown.Intents.General
    ];
}

public static class ModelTierPolicy
{
    public static ModelTier Choose(ReasoningResult? reasoning, bool isCompound, ModelRoutingOptions options)
    {
        if (!options.Enabled || reasoning is null || reasoning.IsFallback || isCompound) return ModelTier.Standard;
        if (reasoning.ConfidenceScore < options.MinConfidence) return ModelTier.Standard;
        // Memnuniyetsiz müşteri en iyi yanıtı hak eder; tonu ucuz model bozmasın.
        if (reasoning.SentimentScore < WellKnown.SentimentThresholds.NegativeThreshold) return ModelTier.Standard;
        return options.EffectiveFastIntents.Contains(reasoning.Intent) ? ModelTier.Fast : ModelTier.Standard;
    }
}

public sealed class AsyncLocalTurnModelTier : ITurnModelTier
{
    private static readonly AsyncLocal<ModelTier?> Value = new();

    public ModelTier Current => Value.Value ?? ModelTier.Standard;

    public IDisposable Begin(ModelTier tier)
    {
        var previous = Value.Value;
        Value.Value = tier;
        return new Scope(previous);
    }

    private sealed class Scope(ModelTier? previous) : IDisposable
    {
        public void Dispose() => Value.Value = previous;
    }
}
