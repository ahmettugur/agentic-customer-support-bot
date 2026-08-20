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
            UpsertedBeforeCleanup ??= Upserted.Count;
            return Task.CompletedTask;
        }

        /// <summary>Temizlik çağrıldığında geçerli olan (tagKey, tagValue).</summary>
        public (string Key, string Value)? Cleanup { get; private set; }

        /// <summary>Temizlik anında kaç belge yazılmıştı — sıranın doğruluğunu ölçmek için.</summary>
        public int? UpsertedBeforeCleanup { get; private set; }

        public Task DeleteStaleAsync(
            MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default)
        {
            Cleanup = (tagKey, tagValue);
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
        public Task DeleteStaleAsync(MemoryKind kind, string tagKey, string tagValue, CancellationToken ct = default)
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

    // ─── Yeniden yükleme: çoğalma ve artık kalan içerik ───────────────────────
    //
    // Dosya chunk'ları her yüklemede rastgele bir Guid alıyordu, yani upsert her seferinde
    // insert'e dönüşüyordu. İki sonucu vardı: değişmeyen dosyalar her turda çoğalıyor ve
    // düzenlenen bir dosyanın ESKİ metni aramada kalmaya devam ediyordu — düzeltilmiş ya da
    // silinmiş bir bilgiyi bot yanıtlamaya devam edebiliyordu.

    /// <summary>
    /// Kimlik dosya yoluna ve chunk sırasına bağlı olmalı ki aynı içerik aynı kaydın üzerine
    /// yazılsın. Rastgele kimlikle bu test, her çalıştırmada farklı değerler görürdü.
    /// </summary>
    [Fact]
    public async Task FileChunks_GetStableIdentities_AcrossReloads()
    {
        var first = new CountingIngestor(existingCount: 0);
        await Build(first).IngestAsync(TestContext.Current.CancellationToken);

        var second = new CountingIngestor(existingCount: 0);
        await Build(second).IngestAsync(TestContext.Current.CancellationToken);

        first.Upserted.Should().NotBeEmpty();
        second.Upserted.Select(d => d.Id).Should().Equal(
            first.Upserted.Select(d => d.Id),
            "aynı kaynak aynı kimlikleri üretmeli — yoksa her yükleme kopya ekler");

        first.Upserted.Select(d => d.Id).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Kararlı kimlik, ARTIK ÜRETİLMEYEN belgeleri çözmez: kaynak dosyası silindiğinde ya da
    /// küçüldüğünde o chunk'ların kimlikleri yeni turda hiç görünmez, dolayısıyla tek tek
    /// silinemezler. Bu yüzden her tur kendi damgasını yazar ve damgası eski olanlar silinir.
    /// </summary>
    [Fact]
    public async Task Reload_StampsDocuments_AndSweepsTheOnesNoLongerProduced()
    {
        var ingestor = new CountingIngestor(existingCount: 0);

        await Build(ingestor).IngestAsync(TestContext.Current.CancellationToken);

        ingestor.Cleanup.Should().NotBeNull("artık üretilmeyen belgeler temizlenmeli");
        var (key, value) = ingestor.Cleanup!.Value;

        ingestor.Upserted.Should().OnlyContain(d => d.Tags.ContainsKey(key) && d.Tags[key] == value,
            "temizlik bu turun damgasını taşımayan her şeyi sileceği için, yazılan HER belge damgalanmış olmalı");
    }

    /// <summary>
    /// Sıra önemli: temizlik yazmadan SONRA olmalı. Önce silinseydi, yazma başarısız olduğunda
    /// bilgi tabanı tamamen boş kalırdı.
    /// </summary>
    [Fact]
    public async Task Cleanup_RunsAfterTheWrite_NotBefore()
    {
        var ingestor = new CountingIngestor(existingCount: 0);

        await Build(ingestor).IngestAsync(TestContext.Current.CancellationToken);

        ingestor.Cleanup.Should().NotBeNull();
        ingestor.UpsertedBeforeCleanup.Should().NotBeNull()
            .And.Be(ingestor.Upserted.Count, "temizlik anında yazma tamamlanmış olmalı");
    }
}
