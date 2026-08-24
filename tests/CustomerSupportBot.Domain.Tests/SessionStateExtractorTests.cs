// Tests/Services/SessionStateExtractorTests.cs
//
// EntityVerifier ve tool güvenlik sınırı yalnız AuthenticatedCustomerId (JWT) kullanır;
// SessionStateExtractor artık metinden ID çıkarmaz (bkz. IdExtractor'ın kaldırılması) — bu
// dosyadaki testler yalnızca intent/sentiment/faz çıkarımını kapsar.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Domain.Tests;

public class SessionStateExtractorTests
{
    private static SessionState Apply(string userMessage, string botResponse = "")
    {
        var state = new SessionState();
        SessionStateExtractor.ExtractAndApply(state, userMessage, botResponse);
        return state;
    }

    [Fact]
    public void ExtractAndApply_IncrementsTurnCountAndRecordsSentiment()
    {
        var state = Apply("Sipariş numaram 1041.");

        state.TurnCount.Should().Be(1);
        state.SentimentHistory.Should().ContainSingle();
        state.SentimentHistory[0].Turn.Should().Be(1);
    }

    // ─── LLM sinyali ↔ kural tabanlı çıkarım önceliği ────────────────────────────
    // ExtractAndApply, turun türetilmiş alanlarının TEK yazarıdır. LLM bir sinyal
    // ürettiyse o kazanır; üretmediyse kural tabanlı tabloya düşülür. Bu öncelik eskiden
    // ters çalışıyordu: LLM'in değeri tur ortasında yazılıp burada eziliyordu.

    [Fact]
    public void ExtractAndApply_LlmIntent_OverridesKeywordDetection()
    {
        var state = new SessionState();

        // "iptal" kelimesi kural tabanlı tabloda sipariş_iptali'ne eşleşir; LLM ise
        // mesajın gerçekte bir iade talebi olduğuna karar vermiş.
        SessionStateExtractor.ExtractAndApply(
            state, "iptal ettiğim ürünü geri göndermek istiyorum", "",
            llm: new TurnSignals(WellKnown.Intents.ReturnRequest, null, null));

        state.CurrentIntent.Should().Be(WellKnown.Intents.ReturnRequest);
    }

    [Fact]
    public void ExtractAndApply_NoLlmIntent_FallsBackToKeywordDetection()
    {
        var state = new SessionState();

        SessionStateExtractor.ExtractAndApply(state, "siparişimi iptal et", "", llm: null);

        state.CurrentIntent.Should().Be(WellKnown.Intents.OrderCancellation);
    }

    [Fact]
    public void ExtractAndApply_LlmSentiment_DrivesCounterInsteadOfKeywords()
    {
        var state = new SessionState();

        // Kural tablosu bu cümlede hiçbir şey bulamaz (nötr 0.5 → sayaç sıfırlanırdı);
        // LLM örtük öfkeyi yakalamış.
        SessionStateExtractor.ExtractAndApply(
            state, "bu durumu bir üst makama taşıyacağım", "",
            llm: new TurnSignals(null, WellKnown.Sentiments.Angry, 0.1));

        state.Sentiment.Should().Be(WellKnown.Sentiments.Angry);
        state.ConsecutiveNegativeTurns.Should().Be(1);
        state.SentimentHistory.Should().ContainSingle()
            .Which.Label.Should().Be(WellKnown.Sentiments.Angry);
    }

    [Fact]
    public void ExtractAndApply_PartialSignal_UsesKeywordsForTheMissingField()
    {
        var state = new SessionState();

        // Yalnızca intent geldi — sentiment kural tabanlı hesaplanmalı.
        SessionStateExtractor.ExtractAndApply(
            state, "ürün bozuk geldi", "",
            llm: new TurnSignals(WellKnown.Intents.Complaint, null, null));

        state.CurrentIntent.Should().Be(WellKnown.Intents.Complaint);
        state.Sentiment.Should().Be(WellKnown.Sentiments.Negative, "'bozuk' kural tablosunda negatif");
    }

    // ─── Kural tabanlı duygu tablosu: işlem adları duygu değildir ────────────────

    [Theory]
    [InlineData("teşekkürler, iade işlemim tamamlandı, çok memnunum")]
    [InlineData("siparişimi iptal etmek istiyorum")]
    [InlineData("şikayet kaydı açmak istiyorum")]
    [InlineData("talebim çözüldü, teşekkür ederim")]
    public void ExtractAndApply_TransactionWords_DoNotCountAsNegativeTurn(string message)
    {
        var state = Apply(message);

        state.ConsecutiveNegativeTurns.Should().Be(0,
            "iade/iptal/şikayet bir işlem adıdır, duygu değil — ardışık negatif sayacını " +
            "artırıp otomatik eskalasyonu yanlış tetiklememeli");
    }

    [Fact]
    public void ExtractAndApply_SubstringMatch_IsAKnownLimitationOfTheKeywordFallback()
    {
        // BİLİNEN SINIR — düzeltilmedi, kayda geçiriliyor.
        // Eşleşme saf substring olduğu için "sorun" kelimesi "sorunum çözüldü" gibi olumlu
        // bir kapanış cümlesinde de yakalanır. Bunu kelime listesinden "sorun"u çıkararak
        // çözmek gerçek olumsuz sinyalleri ("bir sorun var") kaybettirir; düzgün çözümü
        // kelime sınırı + olumsuzlama analizi, yani bu tablonun ötesinde bir iş.
        //
        // Etkisi sınırlı: gerçek boru hattında LLM sentiment'i önceliklidir (bkz. TurnSignals),
        // bu tablo yalnızca LLM sinyal üretmediğinde fallback olarak çalışır.
        var state = Apply("sorunum çözüldü, teşekkür ederim");

        state.Sentiment.Should().Be(WellKnown.Sentiments.Negative,
            "bugünkü davranış bu — değişirse bu test bilinçli olarak güncellenmeli");
    }

    [Fact]
    public void ExtractAndApply_NegativeStillBeatsPositive_WhenItIsAPrefixCase()
    {
        // Sıra load-bearing: "memnun değil" (negative), "memnun" (positive) önekidir.
        var state = Apply("hizmetinizden memnun değilim");

        state.Sentiment.Should().Be(WellKnown.Sentiments.Negative);
    }

}
