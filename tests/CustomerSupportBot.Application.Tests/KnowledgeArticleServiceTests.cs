// Bilgi tabanı makale yönetiminin davranış testleri.
//
// Buradaki asıl risk "kaydetme" değil, DB ile vector indeksinin birbirinden
// kopması: kısalan makalenin artık chunk'ları indekste kalırsa ajanlar silinmiş
// metni kaynak göstermeye devam eder. Testlerin çoğu bu senaryoyu kovalıyor.

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class KnowledgeArticleServiceTests
{
    private readonly FakeArticleStore _store = new();
    private readonly FakeIngestor _memory = new();
    private readonly KnowledgeArticleService _sut;

    public KnowledgeArticleServiceTests()
    {
        var options = new SemanticMemoryOptions();
        options.KnowledgeBase.ChunkSize = 120;
        options.KnowledgeBase.ChunkOverlap = 0;

        _sut = new KnowledgeArticleService(
            _store, _memory, Options.Create(options),
            NullLogger<KnowledgeArticleService>.Instance);
    }

    /// <summary>ChunkSize=120 ile kesin 3 parçaya bölünen metin.</summary>
    private static string ThreeChunkContent()
        => string.Join("\n\n", Enumerable.Range(1, 3).Select(i => $"Paragraf {i}. " + new string('x', 100)));

    private static string OneChunkContent() => "Tek kısa paragraf.";

    // ─── temel indeksleme ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_PublishedArticle_IndexesChunksAndPersistsCount()
    {
        var result = await _sut.CreateAsync("Kargo", ThreeChunkContent(), "kargo", isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        result.Indexed.Should().BeTrue();
        result.Warning.Should().BeNull();
        result.Article.IndexedChunkCount.Should().Be(3);

        _memory.Upserted.Should().HaveCount(3);
        _store.Saved.Should().ContainSingle()
            .Which.IndexedChunkCount.Should().Be(3);
    }

    [Fact]
    public async Task Create_DraftArticle_IsPersistedButNotIndexed()
    {
        var result = await _sut.CreateAsync("Taslak", ThreeChunkContent(), null, isPublished: false, "ahmet", TestContext.Current.CancellationToken);

        result.Indexed.Should().BeTrue("kaydetme başarılı — taslak olmak hata değil");
        result.Article.IndexedChunkCount.Should().Be(0);
        _memory.Upserted.Should().BeEmpty("taslak makale ajanların yanıtlarına sızmamalı");
    }

    [Fact]
    public async Task Create_UsesDeterministicChunkIds()
    {
        var result = await _sut.CreateAsync("Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);
        var id = result.Article.Id;

        _memory.Upserted.Select(d => d.Id).Should().Equal(
            KnowledgeArticle.ChunkId(id, 0),
            KnowledgeArticle.ChunkId(id, 1),
            KnowledgeArticle.ChunkId(id, 2));
    }

    [Fact]
    public async Task Create_TagsChunksWithArticleAndCategory()
    {
        var result = await _sut.CreateAsync("Kargo", OneChunkContent(), "kargo", isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        var doc = _memory.Upserted.Should().ContainSingle().Subject;
        doc.Tags.Should().Contain("article", result.Article.Id);
        doc.Tags.Should().Contain("category", "kargo");
        doc.Source.Should().Be($"article:{result.Article.Id}");
        doc.Kind.Should().Be(MemoryKind.Knowledge);
    }

    // ─── indeks/DB tutarlılığı — asıl risk ───────────────────────────────────

    [Fact]
    public async Task Update_ShrinkingArticle_RemovesOrphanedChunks()
    {
        var created = await _sut.CreateAsync("Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);
        var id = created.Article.Id;
        _memory.Reset();

        var updated = await _sut.UpdateAsync(id, "Kargo", OneChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        updated!.Article.IndexedChunkCount.Should().Be(1);
        _memory.Deleted.Should().Equal(
            KnowledgeArticle.ChunkId(id, 1),
            KnowledgeArticle.ChunkId(id, 2));
    }

    [Fact]
    public async Task Update_Unpublishing_RemovesEveryChunkFromIndex()
    {
        var created = await _sut.CreateAsync("Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);
        var id = created.Article.Id;
        _memory.Reset();

        var updated = await _sut.UpdateAsync(id, "Kargo", ThreeChunkContent(), null, isPublished: false, "ahmet", TestContext.Current.CancellationToken);

        updated!.Article.IndexedChunkCount.Should().Be(0);
        _memory.Upserted.Should().BeEmpty();
        _memory.Deleted.Should().HaveCount(3);
    }

    [Fact]
    public async Task Update_GrowingArticle_DeletesNothing()
    {
        var created = await _sut.CreateAsync("Kargo", OneChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);
        _memory.Reset();

        await _sut.UpdateAsync(created.Article.Id, "Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        _memory.Deleted.Should().BeEmpty();
        _memory.Upserted.Should().HaveCount(3);
    }

    [Fact]
    public async Task Delete_RemovesAllChunksBeforeDroppingTheRow()
    {
        var created = await _sut.CreateAsync("Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);
        var id = created.Article.Id;
        _memory.Reset();

        var deleted = await _sut.DeleteAsync(id, TestContext.Current.CancellationToken);

        deleted.Should().BeTrue();
        _memory.Deleted.Should().HaveCount(3);
        _store.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_UnknownArticle_ReturnsFalseAndTouchesNothing()
    {
        (await _sut.DeleteAsync("yok-böyle-bir-id", TestContext.Current.CancellationToken)).Should().BeFalse();
        _memory.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_UnknownArticle_ReturnsNull()
    {
        var result = await _sut.UpdateAsync("yok", "T", "İçerik", null, true, "ahmet", TestContext.Current.CancellationToken);
        result.Should().BeNull();
    }

    // ─── hata yolları ────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_WhenIndexingFails_KeepsArticleAndReportsWarning()
    {
        _memory.ThrowOnUpsert = true;

        var result = await _sut.CreateAsync("Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        result.Indexed.Should().BeFalse();
        result.Warning.Should().NotBeNullOrWhiteSpace();
        _store.Saved.Should().ContainSingle("indeks patlasa da makale kaybolmamalı");
    }

    [Fact]
    public async Task Save_WhenIndexingFails_DoesNotClaimChunksThatWereNeverWritten()
    {
        // Önce 3 chunk'lı bir makale indekslenir, sonra güncelleme sırasında indeks patlar.
        var created = await _sut.CreateAsync("Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);
        var id = created.Article.Id;
        _memory.ThrowOnUpsert = true;

        var updated = await _sut.UpdateAsync(id, "Kargo", OneChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        // Kayıtlı sayı eski değere dönmeli; aksi halde sonraki kayıt 1..3 aralığını
        // "zaten silinmiş" sayar ve yetim chunk'lar indekste kalıcı olur.
        updated!.Article.IndexedChunkCount.Should().Be(3);
        _store.Saved.Single().IndexedChunkCount.Should().Be(3);
    }

    [Fact]
    public async Task Save_WhenMemoryDisabled_PersistsWithWarning()
    {
        _memory.Enabled = false;

        var result = await _sut.CreateAsync("Kargo", ThreeChunkContent(), null, isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        result.Indexed.Should().BeFalse();
        result.Warning.Should().Contain("Semantic memory");
        _store.Saved.Should().ContainSingle();
        _memory.Upserted.Should().BeEmpty();
    }

    // ─── normalizasyon ───────────────────────────────────────────────────────

    [Fact]
    public async Task Create_TrimsTitleAndNormalizesBlankCategoryToNull()
    {
        var result = await _sut.CreateAsync("  Kargo  ", OneChunkContent(), "   ", isPublished: true, "ahmet", TestContext.Current.CancellationToken);

        result.Article.Title.Should().Be("Kargo");
        result.Article.Category.Should().BeNull();
    }

    [Fact]
    public async Task Update_RecordsEditorAndTimestamp()
    {
        var created = await _sut.CreateAsync("Kargo", OneChunkContent(), null, true, "ahmet", TestContext.Current.CancellationToken);
        var createdAt = created.Article.CreatedAt;

        var updated = await _sut.UpdateAsync(created.Article.Id, "Kargo", OneChunkContent(), null, true, "zeynep", TestContext.Current.CancellationToken);

        updated!.Article.UpdatedBy.Should().Be("zeynep");
        updated.Article.CreatedAt.Should().Be(createdAt, "oluşturma zamanı düzenlemeyle değişmemeli");
        updated.Article.UpdatedAt.Should().BeOnOrAfter(createdAt);
    }

    // ─── test ikizleri ───────────────────────────────────────────────────────

    private sealed class FakeArticleStore : IKnowledgeArticleStore
    {
        private readonly Dictionary<string, KnowledgeArticle> _rows = new(StringComparer.Ordinal);

        public List<KnowledgeArticle> Saved => _rows.Values.ToList();

        public Task<IReadOnlyList<KnowledgeArticle>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KnowledgeArticle>>(_rows.Values.ToList());

        public Task<IReadOnlyList<KnowledgeArticle>> GetPublishedAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<KnowledgeArticle>>(_rows.Values.Where(a => a.IsPublished).ToList());

        // Store gerçek bir DB gibi kopya döner — servis çağıranın nesnesini
        // paylaşırsa testler yanlışlıkla yeşile döner.
        public Task<KnowledgeArticle?> GetAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_rows.TryGetValue(id, out var a) ? Copy(a) : null);

        public Task UpsertAsync(KnowledgeArticle article, CancellationToken ct = default)
        {
            _rows[article.Id] = Copy(article);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_rows.Remove(id));

        private static KnowledgeArticle Copy(KnowledgeArticle a) => new()
        {
            Id = a.Id,
            Title = a.Title,
            Content = a.Content,
            Category = a.Category,
            IsPublished = a.IsPublished,
            IndexedChunkCount = a.IndexedChunkCount,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
            UpdatedBy = a.UpdatedBy
        };
    }

    private sealed class FakeIngestor : ISemanticMemoryIngestor
    {
        public bool Enabled { get; set; } = true;
        public bool IsConfigured { get; set; } = true;
        public bool ThrowOnUpsert { get; set; }

        public List<MemoryDocument> Upserted { get; } = [];
        public List<string> Deleted { get; } = [];

        public void Reset()
        {
            Upserted.Clear();
            Deleted.Clear();
        }

        public Task EnsureCollectionsAsync(CancellationToken ct = default) => Task.CompletedTask;

        /// <summary>Upsert edilen doküman sayısı — ingestion'ın "koleksiyon boş mu" kontrolü için.</summary>
        public Task<long> CountAsync(MemoryKind kind, CancellationToken ct = default) =>
            Task.FromResult((long)Upserted.Count);

        public Task UpsertManyAsync(MemoryKind kind, IReadOnlyList<MemoryDocument> docs, CancellationToken ct = default)
        {
            if (ThrowOnUpsert) throw new InvalidOperationException("vector store erişilemiyor");
            Upserted.AddRange(docs);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(MemoryKind kind, string documentId, CancellationToken ct = default)
        {
            Deleted.Add(documentId);
            return Task.CompletedTask;
        }
    }
}
