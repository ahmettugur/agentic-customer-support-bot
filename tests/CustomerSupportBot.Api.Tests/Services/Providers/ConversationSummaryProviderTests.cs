// Tests/Services/Providers/ConversationSummaryProviderTests.cs
//
// Asıl konu: özetleme ARTIMLI olmalı. Eski davranış (tüm eski geçmişi her turda sıfırdan
// yeniden özetlemek) çıktı olarak doğruydu ama maliyeti azaltmıyor, artırıyordu — bu
// testler LLM'e yalnızca YENİ (henüz özetlenmemiş) mesajların gönderildiğini doğrular.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Services.Providers;

public class ConversationSummaryProviderTests
{
    private static List<ConversationMessage> History(int count) =>
        Enumerable.Range(1, count)
            .Select(i => new ConversationMessage(
                i % 2 == 1 ? ConversationRoles.User : ConversationRoles.Assistant,
                $"mesaj-{i}"))
            .ToList();

    private static AgentSession Session(string? summary = null, int summarizedCount = 0) =>
        new()
        {
            SessionId = "s1",
            State = new SessionState { ConversationSummary = summary, SummarizedMessageCount = summarizedCount }
        };

    private static (ConversationSummaryProvider Provider, IGeneralChatClient ChatClient, ISessionManager Sessions) Build(
        List<ConversationMessage> history)
    {
        var chatClient = Substitute.For<IGeneralChatClient>();
        chatClient.CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>())
            .Returns("yeni özet");

        var sessions = Substitute.For<ISessionManager>();
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(history);

        var distributedLock = new InMemoryDistributedLock(Options.Create(new RedisOptions()));

        var provider = new ConversationSummaryProvider(
            chatClient, sessions, distributedLock, NullLogger<ConversationSummaryProvider>.Instance);

        return (provider, chatClient, sessions);
    }

    [Fact]
    public async Task BelowThreshold_ReturnsNull_NoLlmCall()
    {
        var (provider, chatClient, _) = Build(History(7));

        var result = await provider.GetContextAsync(Session(), "soru");

        result.Should().BeNull();
        await chatClient.DidNotReceive().CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FirstSummarization_SummarizesOnlyOldMessages_NotRecent()
    {
        var history = History(8); // boundary = 8 - 4 = 4
        var (provider, chatClient, sessions) = Build(history);

        var result = await provider.GetContextAsync(Session(), "soru");

        result.Should().Be("[Konuşma Özeti]\nyeni özet");
        await chatClient.Received(1).CompleteAsync(
            Arg.Is<IReadOnlyList<ConversationMessage>>(m =>
                m[1].Text.Contains("mesaj-1") && m[1].Text.Contains("mesaj-4") && !m[1].Text.Contains("mesaj-5")),
            Arg.Any<CancellationToken>());
        await sessions.Received(1).UpdateAsync(
            Arg.Is<AgentSession>(s => s.State.SummarizedMessageCount == 4 && s.State.ConversationSummary == "yeni özet"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Asıl kazancın kanıtı: ikinci turda yalnızca DELTA gönderilir, tüm geçmiş değil.</summary>
    [Fact]
    public async Task IncrementalFold_SendsOnlyNewMessagesSinceLastSummary_NotFullHistory()
    {
        var history = History(20); // boundary = 20 - 4 = 16
        var (provider, chatClient, sessions) = Build(history);
        var session = Session(summary: "önceki özet", summarizedCount: 12);

        var result = await provider.GetContextAsync(session, "soru");

        result.Should().Be("[Konuşma Özeti]\nyeni özet");
        await chatClient.Received(1).CompleteAsync(
            Arg.Is<IReadOnlyList<ConversationMessage>>(m =>
                // Mevcut özet prompt'a katılmalı (fold), yeni mesajlar mesaj-13..16 olmalı,
                // mesaj-1..12 (zaten özetlenmiş) veya mesaj-17..20 (hâlâ ham gidecek) İÇERMEMELİ.
                m[1].Text.Contains("önceki özet") &&
                m[1].Text.Contains("mesaj-13") && m[1].Text.Contains("mesaj-16") &&
                !m[1].Text.Contains("mesaj-12") && !m[1].Text.Contains("mesaj-17")),
            Arg.Any<CancellationToken>());
        await sessions.Received(1).UpdateAsync(
            Arg.Is<AgentSession>(s => s.State.SummarizedMessageCount == 16),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CacheHit_BoundaryAlreadyCovered_NoLlmCall()
    {
        var history = History(10); // boundary = 10 - 4 = 6
        var (provider, chatClient, sessions) = Build(history);
        var session = Session(summary: "kapsayan özet", summarizedCount: 6);

        var result = await provider.GetContextAsync(session, "soru");

        result.Should().Be("[Konuşma Özeti]\nkapsayan özet");
        await chatClient.DidNotReceive().CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>());
        await sessions.DidNotReceive().UpdateAsync(Arg.Any<AgentSession>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LlmThrows_ReturnsNull_DoesNotCorruptState()
    {
        var history = History(8);
        var chatClient = Substitute.For<IGeneralChatClient>();
        chatClient.CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("llm down"));
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(history);
        var distributedLock = new InMemoryDistributedLock(Options.Create(new RedisOptions()));
        var provider = new ConversationSummaryProvider(
            chatClient, sessions, distributedLock, NullLogger<ConversationSummaryProvider>.Instance);
        var session = Session();

        var result = await provider.GetContextAsync(session, "soru");

        result.Should().BeNull();
        session.State.ConversationSummary.Should().BeNull();
        session.State.SummarizedMessageCount.Should().Be(0);
    }
}
