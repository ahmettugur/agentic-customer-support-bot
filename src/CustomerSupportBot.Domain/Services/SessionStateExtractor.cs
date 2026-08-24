// Domain/Services/SessionStateExtractor.cs
// Oturum durumu çıkarma mantığı — hem InMemory hem Postgres adaptörleri tarafından kullanılır.
// Tekrarlanan intent/sentiment/phase algılama kodunu tek doğruluk kaynağı olarak merkezîleştirir.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

/// <summary>
/// Kullanıcı ve bot mesajlarından oturum durumunu (intent, sentiment, phase, ID'ler) çıkarır.
/// Saf domain servisi — hiçbir infrastructure bağımlılığı yoktur.
/// </summary>
public static class SessionStateExtractor
{
    /// <summary>
    /// Bir konuşma turundaki user ve bot mesajlarından state bilgilerini günceller.
    ///
    /// <para>
    /// <b>Bu metot turun türetilmiş alanlarının (intent, sentiment,
    /// <see cref="SessionState.ConsecutiveNegativeTurns"/>, faz) TEK YAZARIDIR.</b>
    /// Başka hiçbir yerden yazılmamalıdır — LLM'in ürettiği değerler
    /// <paramref name="llm"/> ile girdi olarak buraya taşınır. Bu alanları turun ortasında
    /// ayrıca yazan ikinci bir yol eklemek, sayacın tur başına iki kez ilerlemesine ve
    /// otomatik eskalasyonun erken tetiklenmesine yol açar (bkz. <see cref="TurnSignals"/>).
    /// </para>
    /// </summary>
    /// <param name="priorHistory">
    /// Bu turdan ÖNCEKİ konuşma turları (eski → yeni sıralı, opsiyonel). Şu an bu metotta
    /// kullanılmıyor — imza geriye dönük uyumluluk için korunuyor (bkz. IdExtractor'ın
    /// kaldırılması: metinden ID çıkarımı ve bağlam-sürekliliği mantığı tamamen kalktı).
    /// </param>
    /// <param name="llm">
    /// LLM reasoning'inin bu tur için ürettiği sinyaller (opsiyonel). Dolu olan her alan
    /// kural tabanlı çıkarımın YERİNE geçer; <c>null</c> alanlarda
    /// <see cref="DetectUserIntent"/> / <see cref="DetectSentiment"/> devreye girer.
    /// </param>
    public static void ExtractAndApply(
        SessionState state, string userMessage, string botResponse,
        IReadOnlyList<ConversationMessage>? priorHistory = null,
        TurnSignals? llm = null)
    {
        state.TurnCount++;

        // Niyet tespiti — LLM bir karar ürettiyse o kazanır, yoksa kural tabanlı tabloya düş.
        state.CurrentIntent = llm?.Intent ?? DetectUserIntent(userMessage);

        // Faz belirleme
        state.Phase = DetermineConversationPhase(state.TurnCount, botResponse);

        // Duygu analizi — aynı öncelik. Karar TEK yerde verilir; aşağıdaki tüm türetmeler
        // (state alanları, SentimentHistory, ConsecutiveNegativeTurns) bu tek sonuca dayanır.
        var (sentimentLabel, sentimentScore) = llm is { SentimentLabel: not null, SentimentScore: not null }
            ? (llm.SentimentLabel, llm.SentimentScore.Value)
            : DetectSentiment(userMessage);

        state.Sentiment = sentimentLabel;
        state.SentimentScore = sentimentScore;

        state.SentimentHistory.Add(new SentimentEntry
        {
            Turn = state.TurnCount,
            Label = sentimentLabel,
            Score = sentimentScore
        });

        // Son 20 tur dışını temizle
        if (state.SentimentHistory.Count > 20)
            state.SentimentHistory.RemoveRange(0, state.SentimentHistory.Count - 20);

        // Ardışık negatif sayacı
        if (sentimentScore < WellKnown.SentimentThresholds.NegativeThreshold)
            state.ConsecutiveNegativeTurns++;
        else
            state.ConsecutiveNegativeTurns = 0;
    }

    /// <summary>
    /// Kullanıcı mesajından niyet algılar.
    /// Önce WellKnown.IntentKeywords mapping'i, sonra özel kompozit kural.
    /// </summary>
    public static string DetectUserIntent(string message)
    {
        var lower = message.ToLowerInvariant();

        // Özel kural: "sipariş" + durum/takip/nerede → OrderInquiry
        if (lower.Contains("sipariş") &&
            (lower.Contains("durum") || lower.Contains("takip") || lower.Contains("nerede")))
        {
            return WellKnown.Intents.OrderInquiry;
        }

        // Tablo tabanlı eşleşme — ilk eşleşen niyet seçilir
        foreach (var (intent, keywords) in WellKnown.IntentKeywords)
        {
            if (keywords.Any(k => lower.Contains(k)))
            {
                return intent;
            }
        }

        return WellKnown.Intents.General;
    }

    /// <summary>
    /// Tur sayısı ve bot yanıtından konuşma fazını belirler.
    /// </summary>
    public static string DetermineConversationPhase(int turnCount, string botResponse)
    {
        return turnCount switch
        {
            1 => WellKnown.Phases.Inquiry,
            _ when botResponse.Contains(WellKnown.ResponseKeywords.SuccessMarker, StringComparison.OrdinalIgnoreCase) => WellKnown.Phases.Resolution,
            _ when botResponse.Contains(WellKnown.ResponseKeywords.MissingInfoMarker, StringComparison.OrdinalIgnoreCase) => WellKnown.Phases.Inquiry,
            _ => WellKnown.Phases.Action
        };
    }

    /// <summary>
    /// Kural tabanlı hızlı duygu analizi. WellKnown.SentimentKeywords tablosunu kullanır.
    /// </summary>
    public static (string Label, double Score) DetectSentiment(string message)
    {
        var lower = message.ToLowerInvariant();

        foreach (var (sentiment, score, keywords) in WellKnown.SentimentKeywords)
        {
            if (keywords.Any(k => lower.Contains(k)))
            {
                return (sentiment, score);
            }
        }

        return (WellKnown.Sentiments.Neutral, 0.5);
    }

}
