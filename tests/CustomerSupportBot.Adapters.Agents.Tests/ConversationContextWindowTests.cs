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
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Adapters.Agents.Tests;

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

    /// <summary>Özetin bu turda prompt'a GİRDİĞİ durum.</summary>
    private static ContextResult SummaryIncluded() => new("[Konuşma Özeti]\n…", [
        new ContextPart(ConversationSummaryProvider.ProviderName, 5, ContextPartStatus.Included, 20)
    ]);

    /// <summary>Özetleyicinin düştüğü durum — özet prompt'a girmedi.</summary>
    private static ContextResult SummaryMissing(ContextPartStatus status) => new("", [
        new ContextPart(ConversationSummaryProvider.ProviderName, 5, status, 0)
    ]);

    // ═══ Özet varken: özetlenen aralık atlanır ═══

    [Fact]
    public void Summarized_SkipsSummarizedPrefix_KeepsRest()
    {
        var history = History(12);
        var session = SessionWith("özet metni", summarizedCount: 8);

        var sent = WorkflowMessageBuilder.SelectHistoryToSend(history, session, SummaryIncluded()).ToList();

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
            .SelectHistoryToSend(history, SessionWith(summary: null, summarizedCount: 0), SummaryIncluded()).ToList();
        var withSummary = WorkflowMessageBuilder
            .SelectHistoryToSend(history, session, SummaryIncluded()).ToList();

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
            .SelectHistoryToSend(history, SessionWith(summary: null, summarizedCount: 0), SummaryIncluded()).ToList();

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
            .SelectHistoryToSend(history, SessionWith(summary: null, summarizedCount: 6), SummaryIncluded()).ToList();

        sent.Should().HaveCount(10);
    }

    // ═══ Özet bu tur prompt'a girmediyse: kırpma YAPILMAZ ═══

    /// <summary>
    /// Regresyon koruması. Özetleyici hata verir/zaman aşımına uğrarsa özet prompt'a girmez,
    /// ama <c>SessionState.ConversationSummary</c> önceki turdan kalma değerini korur. Karar
    /// yalnızca oturum durumuna bakılarak verilseydi geçmiş yine kırpılır ve o turlar
    /// <b>ne özet ne ham</b> hâlde prompt'a girer — modelin görüş alanından tamamen kaybolurdu.
    /// </summary>
    [Theory]
    [InlineData(ContextPartStatus.Failed)]
    [InlineData(ContextPartStatus.TimedOut)]
    [InlineData(ContextPartStatus.Dropped)]
    [InlineData(ContextPartStatus.Empty)]
    public void SummaryNotIncludedThisTurn_SendsFullHistory(ContextPartStatus status)
    {
        var history = History(12);
        var session = SessionWith("önceki turdan kalma özet", summarizedCount: 8);

        var sent = WorkflowMessageBuilder
            .SelectHistoryToSend(history, session, SummaryMissing(status)).ToList();

        sent.Should().HaveCount(12, "özet prompt'ta yoksa hiçbir tur atlanmamalı");
        sent[0].Text.Should().Be("mesaj-1");
    }

    /// <summary>Bağlamda özet provider'ı hiç görünmüyorsa da kırpma yapılmamalı.</summary>
    [Fact]
    public void SummaryProviderAbsentFromContext_SendsFullHistory()
    {
        var history = History(12);
        var session = SessionWith("özet", summarizedCount: 8);

        var sent = WorkflowMessageBuilder
            .SelectHistoryToSend(history, session, ContextResult.Empty).ToList();

        sent.Should().HaveCount(12);
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
            .SelectHistoryToSend(history, SessionWith("özet", summarized), SummaryIncluded()).ToList();

        sent.Should().HaveCount(historyCount);
    }

    [Fact]
    public void EmptyOrNullHistory_ReturnsEmpty()
    {
        WorkflowMessageBuilder.SelectHistoryToSend(null, SessionWith("özet", 3), SummaryIncluded()).Should().BeEmpty();
        WorkflowMessageBuilder.SelectHistoryToSend([], SessionWith("özet", 3), SummaryIncluded()).Should().BeEmpty();
    }

    [Fact]
    public void NullSession_SendsFullHistory()
    {
        var history = History(7);

        WorkflowMessageBuilder.SelectHistoryToSend(history, session: null, SummaryIncluded())
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
