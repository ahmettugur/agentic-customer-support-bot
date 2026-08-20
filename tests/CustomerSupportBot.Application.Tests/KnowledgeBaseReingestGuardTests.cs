// KnowledgeBase ingest'inin "kaynak değişmedi" kısayolu.
//
// Kısayol iki ayrı soruyu tek soruya indirgiyordu: "kaynak değişti mi" ile "indeks dolu mu".
// İkisi ayrıştığında sonuç sessiz bir veri kaybıdır — koleksiyon dışarıdan yeniden
// oluşturulduğunda (ör. embedding boyutu değişimi) veri gider, kaynak aynı kalır, hash eşleşir
// ve re-ingest atlanır. Arama hiç sonuç döndürmez ama hiçbir hata da görünmez.

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CustomerSupportBot.Application.Tests;

public class KnowledgeBaseReingestGuardTests
{
    private const string Hash = "sabit-hash";

    /// <summary>Kaynağı hep "değişmemiş" gösteren sahte: hash her zaman aynı.</summary>
    private sealed class UnchangedSource : IKnowledgeBaseSource
    {
        public bool Exists => true;
        public string ComputeDirectoryHash() => "dir";
        public string? ReadStateHash() => "dir|" + KnowledgeBaseIngestionService.ComputeArticlesFingerprint([]);
        public void WriteStateHash(string hash) { }

        public async IAsyncEnumerable<KnowledgeBaseFile> ReadFilesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new KnowledgeBaseFile("kb.md", "İade Politikası", "İade politikası: ürünler 14 gün içinde iade edilebilir.");
            await Task.CompletedTask;
        }
    }

    private sealed class CountingIngestor(long existingCount) : ISemanticMemoryIngestor
    {
        public bool Enabled => true;
        public bool IsConfigured => true;
        public List<MemoryDocument> Upserted { get; } = [];

        public Task EnsureCollectionsAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default) => Task.FromResult(existingCount);

        public Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default)
        {
            Upserted.AddRange(docs);
            return Task.CompletedTask;
        }
    }

    private static KnowledgeBaseIngestionService Build(CountingIngestor ingestor)
    {
        var articles = Substitute.For<IKnowledgeArticleStore>();
        articles.GetPublishedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeArticle>>([]));

        return new KnowledgeBaseIngestionService(
            new UnchangedSource(),
            articles,
            ingestor,
            Options.Create(new SemanticMemoryOptions()),
            NullLogger<KnowledgeBaseIngestionService>.Instance);
    }

    [Fact]
    public async Task Ingest_WhenSourceUnchangedAndCollectionEmpty_ReloadsAnyway()
    {
        var ingestor = new CountingIngestor(existingCount: 0);

        await Build(ingestor).IngestAsync(TestContext.Current.CancellationToken);

        ingestor.Upserted.Should().NotBeEmpty(
            "hash 'kaynak değişmedi' der ama koleksiyon boş — atlamak sessizce boş bir bilgi tabanı bırakır");
    }

    [Fact]
    public async Task Ingest_WhenSourceUnchangedAndCollectionPopulated_IsSkipped()
    {
        // Karşı yön: koruma, olağan durumda gereksiz yeniden yüklemeye yol açmamalı.
        var ingestor = new CountingIngestor(existingCount: 42);

        await Build(ingestor).IngestAsync(TestContext.Current.CancellationToken);

        ingestor.Upserted.Should().BeEmpty("kaynak da indeks de yerinde; iş yapılmamalı");
    }

    /// <summary>Koleksiyon hazırlığında yapılandırma hatası fırlatan sahte.</summary>
    private sealed class FailingIngestor(Exception failure) : ISemanticMemoryIngestor
    {
        public bool Enabled => true;
        public bool IsConfigured => true;
        public Task EnsureCollectionsAsync(CancellationToken ct = default) => Task.FromException(failure);
        public Task DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default) => Task.FromResult(0L);
        public Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static KnowledgeBaseIngestionService BuildWith(ISemanticMemoryIngestor ingestor)
    {
        var articles = Substitute.For<IKnowledgeArticleStore>();
        articles.GetPublishedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<KnowledgeArticle>>([]));

        return new KnowledgeBaseIngestionService(
            new UnchangedSource(), articles, ingestor,
            Options.Create(new SemanticMemoryOptions()),
            NullLogger<KnowledgeBaseIngestionService>.Instance);
    }

    /// <summary>
    /// Boyut uyuşmazlığı kararı ingestion katmanında da YUTULMAMALI.
    ///
    /// <para>
    /// Aynı yutma iki katmanda vardı: adaptörde düzeltildi ama çağıran burada tekrarlıyordu.
    /// Yutulduğunda uygulama başlar ve yanlış boyutlu koleksiyonla çalışmaya devam eder —
    /// yani "veri kaybı bilinçli bir karar olmalı" güvencesi kâğıt üzerinde kalır.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ingest_WhenCollectionSetupRejectsConfiguration_PropagatesTheFailure()
    {
        var service = BuildWith(new FailingIngestor(
            new InvalidOperationException("AllowDestructiveDimensionMigration")));

        var act = () => service.IngestAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Karşı yön: vektör deposuna ULAŞILAMAMASI geçici bir altyapı sorunudur; servis ayakta
    /// kalmalı ve ingest sessizce atlanmalıdır.
    /// </summary>
    [Fact]
    public async Task Ingest_WhenVectorStoreIsUnreachable_IsSkippedWithoutThrowing()
    {
        var service = BuildWith(new FailingIngestor(new HttpRequestException("bağlanılamadı")));

        var act = () => service.IngestAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }
}
