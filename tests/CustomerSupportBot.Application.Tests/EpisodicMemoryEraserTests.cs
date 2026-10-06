// Kişisel veri silme: episodik bellekteki (Qdrant) oturum ve müşteri kayıtları.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Application.Services.Privacy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class EpisodicMemoryEraserTests
{
    private static (EpisodicMemoryEraser Eraser, IVectorMemoryPort Store, string Collection) Build(bool enabled = true)
    {
        var store = Substitute.For<IVectorMemoryPort>();
        var options = new SemanticMemoryOptions { Enabled = enabled };
        var memory = new SemanticMemoryService(store, Substitute.For<IEmbeddingPort>(),
            Substitute.For<IContextSanitizer>(), Options.Create(options), NullLogger<SemanticMemoryService>.Instance);
        return (new EpisodicMemoryEraser(memory), store, options.Collections.Episodic);
    }

    [Fact]
    public async Task EraseSessions_DeletesTheSessionsEpisodes()
    {
        var (eraser, store, collection) = Build();

        var n = await eraser.EraseSessionsAsync(["s1", "s2"], TestContext.Current.CancellationToken);

        n.Should().Be(2);
        await store.Received(1).DeleteBySessionsAsync(collection,
            Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "s1", "s2" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EraseCustomer_DeletesEpisodesTaggedWithTheCustomer()
    {
        var (eraser, store, collection) = Build();

        await eraser.EraseCustomerAsync("1001", TestContext.Current.CancellationToken);

        await store.Received(1).DeleteWhereTagAsync(collection, "customerId", "1001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MemoryDisabled_IsANoOp()
    {
        var (eraser, store, _) = Build(enabled: false);

        await eraser.EraseSessionsAsync(["s1"], TestContext.Current.CancellationToken);
        await eraser.EraseCustomerAsync("1001", TestContext.Current.CancellationToken);

        store.ReceivedCalls().Should().BeEmpty();
    }
}
