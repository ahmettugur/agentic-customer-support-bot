// Domain/Model/TurnSignals.cs
// Bir konuşma turunda LLM reasoning'inin ürettiği ve session state'e işlenecek sinyaller.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Bir turda LLM'in ürettiği, <see cref="SessionState"/>'e işlenecek sinyaller.
/// <c>null</c> bir alan "LLM bu sinyali üretmedi" demektir — o alanda
/// <see cref="Services.SessionStateExtractor"/>'ın kural tabanlı çıkarımı devreye girer.
///
/// <para>
/// <b>Neden var:</b> Bu değerler eskiden turun ORTASINDA doğrudan state'e yazılıyordu
/// (<c>ChatPortService</c> → <c>SessionStateService.UpdateSessionIntentAsync</c> /
/// <c>UpdateSessionSentiment</c>), ardından tur kapanırken <c>AddExchangeAsync</c> →
/// <see cref="Services.SessionStateExtractor.ExtractAndApply"/> aynı alanları kural tabanlı
/// değerlerle bir kez daha yazıyordu. İki sonuç doğuruyordu:
/// </para>
/// <list type="number">
/// <item>LLM'in intent'i ve sentiment'i her turda sessizce eziliyordu (belgelenen
/// "LLM daha doğru, kural tabanlıyı override eder" davranışının tam tersi).</item>
/// <item><see cref="SessionState.ConsecutiveNegativeTurns"/> iki farklı yerden
/// artırıldığı için tur başına <b>iki</b> ilerliyordu; otomatik eskalasyon eşiği
/// (<see cref="WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative"/>)
/// 3 tur yerine 2 turda aşılıyordu.</item>
/// </list>
/// <para>
/// Artık bu sinyaller state'e doğrudan yazılmaz; turun kapandığı tek noktaya
/// (<c>ExtractAndApply</c>) <b>girdi</b> olarak taşınır. Türetilmiş alanların tek yazarı
/// oradadır, dolayısıyla çift sayım yapısal olarak imkânsızdır.
/// </para>
/// </summary>
/// <param name="Intent">LLM'in nihai niyet kararı (<see cref="ReasoningResult.Intent"/>).</param>
/// <param name="SentimentLabel">LLM'in duygu etiketi (<see cref="WellKnown.Sentiments"/>).</param>
/// <param name="SentimentScore">LLM'in duygu skoru (0.0–1.0).</param>
public sealed record TurnSignals(
    string? Intent,
    string? SentimentLabel,
    double? SentimentScore,
    double? ConfidenceScore = null)
{
    /// <summary>
    /// Reasoning sonucundan sinyalleri süzer. "Üretilmedi" sayılan değerler <c>null</c>'a
    /// çevrilir, böylece kural tabanlı fallback devreye girebilir:
    /// <list type="bullet">
    /// <item>boş intent veya <see cref="WellKnown.Intents.Unknown"/> → intent yok
    /// (parser JSON'u okuyamadığında bu değeri koyar; onu gerçek bir karar saymak
    /// oturumun niyetini "bilinmiyor"a düşürürdü),</item>
    /// <item>boş sentiment etiketi veya <c>neutral@0.5</c> → sentiment yok
    /// (bu, parser'ın alan hiç dönmediğinde ürettiği varsayılan çift).</item>
    /// </list>
    /// </summary>
    public static TurnSignals? From(ReasoningResult? reasoning)
    {
        if (reasoning is null) return null;

        var intent = string.IsNullOrWhiteSpace(reasoning.Intent) ||
                     reasoning.Intent == WellKnown.Intents.Unknown
            ? null
            : reasoning.Intent;

        var hasSentiment = !string.IsNullOrWhiteSpace(reasoning.Sentiment) &&
                           !(reasoning.Sentiment == WellKnown.Sentiments.Neutral &&
                             reasoning.SentimentScore == 0.5);

        // Güven: yalnızca gerçek bir reasoning'den. Zaman aşımı/hata yer tutucusu (IsFallback) botun isteği
        // anlayamadığını göstermez. Düşük skor tek başına da sinyaldir (intent/sentiment olmasa bile).
        // 0.5 parser'ın alan hiç dönmediğinde koyduğu varsayılandır (neutral@0.5 gibi) → sinyal sayılmaz.
        double? confidence = reasoning.IsFallback || reasoning.ConfidenceScore == 0.5 ? null : reasoning.ConfidenceScore;
        var lowConfidence = confidence is < WellKnown.ConfidenceThresholds.Low;

        if (intent is null && !hasSentiment && !lowConfidence) return null;

        return new TurnSignals(
            intent,
            hasSentiment ? reasoning.Sentiment : null,
            hasSentiment ? reasoning.SentimentScore : null,
            confidence);
    }
}
