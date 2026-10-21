// Hibrit arama yönlendirmesi: ne zaman sözcüksel sinyal de kullanılır, ne zaman yalnızca dense.
// Varsayılan kapalıdır; açıkken yalnızca sorgu METNİ varsa hibrit çalışır (hazır vektörle arayanlar metinsizdir).

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class SemanticMemoryHybridRoutingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SemanticMemoryService Build(IVectorMemoryPort store, bool hybrid)
    {
        var embedder = Substitute.For<IEmbeddingPort>();
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new float[] { 0.1f, 0.2f });
        var options = new SemanticMemoryOptions { Enabled = true };
        options.Retrieval.Hybrid.Enabled = hybrid;
        return new SemanticMemoryService(store, embedder, new ContextSanitizer(), Options.Create(options),
            NullLogger<SemanticMemoryService>.Instance);
    }

    private static IVectorMemoryPort Store()
    {
        var store = Substitute.For<IVectorMemoryPort>();
        store.SearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>()).Returns([]);
        store.HybridSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<float>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>()).Returns([]);
        return store;
    }

    [Fact]
    public async Task SearchByVector_HybridEnabledWithQueryText_UsesHybridSearch()
    {
        var store = Store();

        await Build(store, hybrid: true).SearchByVectorAsync(MemoryKind.Knowledge, [0.1f], queryText: "XK-9921", ct: Ct);

        await store.Received(1).HybridSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), "XK-9921",
            Arg.Any<int>(), Arg.Any<float>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchByVector_HybridDisabled_UsesDenseSearchEvenWithQueryText()
    {
        var store = Store();

        await Build(store, hybrid: false).SearchByVectorAsync(MemoryKind.Knowledge, [0.1f], queryText: "XK-9921", ct: Ct);

        await store.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().HybridSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<string>(),
            Arg.Any<int>(), Arg.Any<float>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchByVector_HybridEnabledButNoQueryText_UsesDenseSearch()
    {
        var store = Store();

        await Build(store, hybrid: true).SearchByVectorAsync(MemoryKind.Knowledge, [0.1f], ct: Ct);

        await store.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<float>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Search_ByText_PassesTheQueryTextToHybridSearch()
    {
        var store = Store();

        await Build(store, hybrid: true).SearchAsync(MemoryKind.Knowledge, "iade kodu ZQ-4471", ct: Ct);

        await store.Received(1).HybridSearchAsync(Arg.Any<string>(), Arg.Any<float[]>(), "iade kodu ZQ-4471",
            Arg.Any<int>(), Arg.Any<float>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }
}
