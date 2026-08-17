// Tests/Services/ConversationContextWindowTests.cs
//
// Bağlam penceresi davranışı: özet, özetlediği turların YERİNE geçmeli — yanına değil.
//
// Düzeltilen hata: ConversationSummaryProvider 8. mesajdan itibaren eski turları özetleyip
// bağlama koyuyordu, ama WorkflowMessageBuilder geçmişi yine baştan sona ekliyordu. Yani aynı
// turlar hem özet hem ham hâliyle gönderiliyor, üstüne özeti üretmek için fazladan bir LLM
// çağrısı yapılıyordu — özetleme maliyeti azaltmak yerine ARTIRIYORDU. Hiçbir test bunu
// yakalamıyordu çünkü çıktı "doğru"ydu, sadece pahalıydı.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Api.Tests.Services;

public class ConversationContextWindowTests
{
    private static List<ConversationMessage> History(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new ConversationMessage(
                i % 2 == 1 ? ConversationRoles.User : ConversationRoles.Assistant,
                $"mesaj-{i}"))
            .ToList();

    private static AgentSession SessionWith(string? summary, int summarizedCount)
    {
        var s = new AgentSession { SessionId = "s1" };
        s.State.ConversationSummary = summary;
        s.State.SummarizedMessageCount = summarizedCount;
        return s;
    }

    // ═══ Özet varken: özetlenen aralık atlanır ═══

    [Fact]
    public void Summarized_SkipsSummarizedPrefix_KeepsRest()
    {
        var history = History(12);
        var session = SessionWith("özet metni", summarizedCount: 8);

        var sent = WorkflowMessageBuilder.SelectHistoryToSend(history, session).ToList();

        sent.Should().HaveCount(4);
        sent[0].Text.Should().Be("mesaj-9");
        sent[^1].Text.Should().Be("mesaj-12");
    }

    /// <summary>
    /// Asıl kazancın kanıtı: aynı geçmiş için gönderilen içerik gerçekten küçülmeli.
    /// Bu test kırmızıya dönerse özet yine "ekleniyor" demektir.
    /// </summary>
    [Fact]
    public void Summarized_ActuallyReducesPayload()
    {
        var history = History(20);
        var session = SessionWith("özet metni", summarizedCount: 16);

        var withoutSummary = WorkflowMessageBuilder
            .SelectHistoryToSend(history, SessionWith(summary: null, summarizedCount: 0)).ToList();
        var withSummary = WorkflowMessageBuilder
            .SelectHistoryToSend(history, session).ToList();

        withSummary.Count.Should().BeLessThan(withoutSummary.Count);
        TokenEstimator.Estimate(withSummary.Select(m => m.Text))
            .Should().BeLessThan(TokenEstimator.Estimate(withoutSummary.Select(m => m.Text)));
    }

    // ═══ Özet yokken: hiçbir şey atlanmaz ═══

    [Fact]
    public void NoSummary_SendsFullHistory()
    {
        var history = History(6);

        var sent = WorkflowMessageBuilder
            .SelectHistoryToSend(history, SessionWith(summary: null, summarizedCount: 0)).ToList();

        sent.Should().HaveCount(6);
    }

    /// <summary>
    /// Sayı state'te kalmış ama özet yoksa atlama YAPILMAMALI — aksi halde özetlenmemiş
    /// mesajlar sessizce düşerdi.
    /// </summary>
    [Fact]
    public void CountWithoutSummary_IsIgnored()
    {
        var history = History(10);

        var sent = WorkflowMessageBuilder
            .SelectHistoryToSend(history, SessionWith(summary: null, summarizedCount: 6)).ToList();

        sent.Should().HaveCount(10);
    }

    // ═══ Savunmacı sınırlar ═══

    /// <summary>
    /// Sayı geçmişten büyükse (geçmiş temizlenmiş, state kalmış) hiçbir şey atlanmaz —
    /// fazladan mesaj göndermek, özetlenmemiş mesajı düşürmekten iyidir.
    /// </summary>
    [Theory]
    [InlineData(5, 5)]
    [InlineData(5, 9)]
    public void CountAtOrBeyondHistoryLength_SendsEverything(int historyCount, int summarized)
    {
        var history = History(historyCount);

        var sent = WorkflowMessageBuilder
            .SelectHistoryToSend(history, SessionWith("özet", summarized)).ToList();

        sent.Should().HaveCount(historyCount);
    }

    [Fact]
    public void EmptyOrNullHistory_ReturnsEmpty()
    {
        WorkflowMessageBuilder.SelectHistoryToSend(null, SessionWith("özet", 3)).Should().BeEmpty();
        WorkflowMessageBuilder.SelectHistoryToSend([], SessionWith("özet", 3)).Should().BeEmpty();
    }

    [Fact]
    public void NullSession_SendsFullHistory()
    {
        var history = History(7);

        WorkflowMessageBuilder.SelectHistoryToSend(history, session: null)
            .Should().HaveCount(7);
    }

    // ═══ Token tahmincisi ═══

    /// <summary>
    /// Mutlak değer değil, <b>oran</b> güvenilir olmalı — aracın tek işi budur.
    /// </summary>
    [Fact]
    public void TokenEstimator_GrowsWithContent()
    {
        var small = TokenEstimator.Estimate(History(4).Select(m => m.Text));
        var large = TokenEstimator.Estimate(History(40).Select(m => m.Text));

        small.Should().BeGreaterThan(0);
        large.Should().BeGreaterThan(small * 5);
    }

    [Fact]
    public void TokenEstimator_EmptyInput_IsZero()
    {
        TokenEstimator.Estimate((string?)null).Should().Be(0);
        TokenEstimator.Estimate("").Should().Be(0);
        TokenEstimator.Estimate(Array.Empty<string?>()).Should().Be(0);
    }
}
