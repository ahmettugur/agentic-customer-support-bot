using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresMessageFeedbackStoreTests(PostgresCatalogFixture fixture) : MessageFeedbackStoreContract
{
    protected override IMessageFeedbackStore Create() => new PostgresMessageFeedbackStore(fixture.DbFactory);

    [Fact]
    public async Task Erase_RemovesTheScoresFromTheObservabilityToolFirst_ThenTheRows()
    {
        var sink = Substitute.For<IConversationScoreSink>();
        var store = new PostgresMessageFeedbackStore(fixture.DbFactory, sink);
        var s = NewSession();
        await store.UpsertAsync(Fb(s, "a", false, "wrong", "kişisel yorum"), Ct);
        await store.UpsertAsync(Fb(s, "b", true), Ct);

        var erased = await store.EraseSessionsAsync([s], Ct);

        Assert.Equal(2, erased);
        Assert.Empty(await store.GetForSessionAsync(s, Ct));
        await sink.Received(1).RemoveMessageFeedbackAsync(s, MessageFeedback.HashOf("a"), Arg.Any<CancellationToken>());
        await sink.Received(1).RemoveMessageFeedbackAsync(s, MessageFeedback.HashOf("b"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Erase_WhenTheToolFails_KeepsTheRows_SoTheErasureCanBeRetried()
    {
        var sink = Substitute.For<IConversationScoreSink>();
        sink.RemoveMessageFeedbackAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("langfuse down")));
        var store = new PostgresMessageFeedbackStore(fixture.DbFactory, sink);
        var s = NewSession();
        await store.UpsertAsync(Fb(s, "a", false), Ct);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.EraseSessionsAsync([s], Ct));

        Assert.Single(await store.GetForSessionAsync(s, Ct));
    }

    [Fact]
    public async Task Erase_OnlyTouchesTheGivenSessions()
    {
        var store = new PostgresMessageFeedbackStore(fixture.DbFactory);
        var gone = NewSession();
        var kept = NewSession();
        await store.UpsertAsync(Fb(gone, "a", true), Ct);
        await store.UpsertAsync(Fb(kept, "a", true), Ct);

        await store.EraseSessionsAsync([gone], Ct);

        Assert.Empty(await store.GetForSessionAsync(gone, Ct));
        Assert.Single(await store.GetForSessionAsync(kept, Ct));
    }
}
