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
    /// </summary>
    public static void ExtractAndApply(SessionState state, string userMessage, string botResponse)
    {
        state.TurnCount++;

        // ID çıkarma — IdExtractor üzerinden (Türkçe bağlam + 4+ haneli rakam)
        var extracted = IdExtractor.Extract(userMessage);
        if (!string.IsNullOrEmpty(extracted.CustomerId))
            state.CustomerId = extracted.CustomerId;
        else if (state.CustomerId is null)
        {
            var fromBot = IdExtractor.Extract(botResponse);
            if (!string.IsNullOrEmpty(fromBot.CustomerId))
                state.CustomerId = fromBot.CustomerId;
        }

        if (!string.IsNullOrEmpty(extracted.OrderId))
            state.CollectedInfo["LastMentionedOrderId"] = extracted.OrderId;

        // Niyet tespiti
        state.CurrentIntent = DetectUserIntent(userMessage);

        // Faz belirleme
        state.Phase = DetermineConversationPhase(state.TurnCount, botResponse);

        // Duygu analizi
        var (sentimentLabel, sentimentScore) = DetectSentiment(userMessage);
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
