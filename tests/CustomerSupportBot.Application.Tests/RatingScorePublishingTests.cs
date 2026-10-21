// Puan kaydı kalıcıdır; Langfuse'a skor göndermek en iyi gayret işidir: gönderim hatası ya da yavaşlığı
// müşterinin puanını kaybettirmemeli.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CustomerSupportBot.Application.Tests;

public class RatingScorePublishingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (AnalyticsPortService Service, InMemoryRatingStore Store) Build(IConversationScoreSink? sink)
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var approvals = new InMemoryApprovalQueue(Options.Create(new ApprovalOptions()), Substitute.For<IApprovalExecutionRouter>(),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var store = new InMemoryRatingStore(NullLogger<InMemoryRatingStore>.Instance);
        var service = new AnalyticsPortService(store, sessions, approvals,
            new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance), NullLogger<AnalyticsPortService>.Instance,
            scoreSink: sink);
        return (service, store);
    }

    [Fact]
    public async Task RateAsync_PublishesSavedRatingToScoreSink()
    {
        var sink = Substitute.For<IConversationScoreSink>();
        var (service, _) = Build(sink);

        var rating = await service.RateAsync("s-1", 5, "harika");

        await sink.Received(1).PublishRatingAsync(
            Arg.Is<ConversationRating>(r => r.SessionId == "s-1" && r.Stars == 5 && r.Feedback == "harika"),
            Arg.Any<CancellationToken>());
        Assert.Equal(5, rating.Stars);
    }

    [Fact]
    public async Task RateAsync_WhenScoreSinkThrows_StillSavesAndReturnsRating()
    {
        var sink = Substitute.For<IConversationScoreSink>();
        sink.PublishRatingAsync(Arg.Any<ConversationRating>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("langfuse down"));
        var (service, store) = Build(sink);

        var rating = await service.RateAsync("s-2", 3);

        Assert.Equal(3, rating.Stars);
        Assert.Equal(3, store.GetBySession("s-2")!.Stars);
    }

    [Fact]
    public async Task RateAsync_WithoutScoreSink_Works()
    {
        var (service, _) = Build(sink: null);

        var rating = await service.RateAsync("s-3", 4);

        Assert.Equal(4, rating.Stars);
    }
}
