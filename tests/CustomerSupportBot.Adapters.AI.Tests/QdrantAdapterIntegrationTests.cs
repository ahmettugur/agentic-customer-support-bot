// Qdrant adaptörünün boyut-uyuşmazlığı dalı — GERÇEK Qdrant'a karşı.
//
// Bu dal uzun süre test edilmedi ve iki kez sessizce kırıldı: önce silme koşulsuzdu, sonra
// fail-closed kararı genel bir catch tarafından yutuldu. İkisi de "kod okununca doğru görünen"
// ama çalıştırılmadıkça fark edilmeyen hatalardı — QdrantDimensionGuardTests yalnızca KARARI
// (RequiresRecreate) ölçer, kararın adaptör içinde gerçekten uygulandığını değil.
//
// Bu yüzden burada sahte yok: gerçek bir Qdrant konteynerine karşı koleksiyon oluşturulur,
// boyut değiştirilir ve adaptörün ne yaptığı ÖLÇÜLÜR.

using CustomerSupportBot.Adapters.AI.Qdrant;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Qdrant;

namespace CustomerSupportBot.Adapters.AI.Tests;

public sealed class QdrantFixture : IAsyncLifetime
{
    private readonly QdrantContainer _container = new QdrantBuilder("qdrant/qdrant:v1.13.4").Build();

    public string Host { get; private set; } = "localhost";
    public int GrpcPort { get; private set; }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        Host = _container.Hostname;
        GrpcPort = _container.GetMappedPublicPort(6334);   // adaptör gRPC kullanıyor
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[Collection("Qdrant")]
public class QdrantAdapterIntegrationTests : IClassFixture<QdrantFixture>
{
    private readonly QdrantFixture _fixture;
    public QdrantAdapterIntegrationTests(QdrantFixture fixture) => _fixture = fixture;

    private QdrantVectorMemoryAdapter Adapter(bool allowDestructive) =>
        new(Options.Create(new SemanticMemoryOptions
        {
            VectorStore = new SemanticMemoryOptions.VectorStoreOptions
            {
                Host = _fixture.Host,
                Port = _fixture.GrpcPort,
                UseHttps = false,
                AllowDestructiveDimensionMigration = allowDestructive
            }
        }),
        NullLogger<QdrantVectorMemoryAdapter>.Instance);

    private static string NewCollection() => $"t_{Guid.NewGuid():N}";

    [Fact]
    public async Task EnsureCollection_CreatesWhenMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        var collection = NewCollection();

        await Adapter(allowDestructive: false).EnsureCollectionAsync(collection, 8, ct);

        (await Adapter(false).CountAsync(collection, ct)).Should().Be(0,
            "koleksiyon var olmalı (yoksa sorgu hata verirdi)");
    }

    /// <summary>Aynı boyutla ikinci çağrı hiçbir şey yapmamalı — veri korunur.</summary>
    [Fact]
    public async Task EnsureCollection_SameDimension_KeepsExistingData()
    {
        var ct = TestContext.Current.CancellationToken;
        var collection = NewCollection();
        var adapter = Adapter(allowDestructive: false);

        await adapter.EnsureCollectionAsync(collection, 8, ct);
        await SeedOnePointAsync(adapter, collection, ct);

        await adapter.EnsureCollectionAsync(collection, 8, ct);

        (await adapter.CountAsync(collection, ct)).Should().Be(1, "boyut aynıyken veri silinmemeli");
    }

    /// <summary>
    /// ASIL KORUMA: boyut değişti, izin yok → fırlat ve <b>veriye dokunma</b>.
    ///
    /// <para>
    /// Burada iki şey birden ölçülüyor: istisnanın adaptörden dışarı ÇIKTIĞI (genel catch
    /// tarafından yutulmadığı) ve koleksiyonun hâlâ eski verisiyle durduğu.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EnsureCollection_DimensionMismatchWithoutPermission_ThrowsAndPreservesData()
    {
        var ct = TestContext.Current.CancellationToken;
        var collection = NewCollection();

        var safe = Adapter(allowDestructive: false);
        await safe.EnsureCollectionAsync(collection, 8, ct);
        await SeedOnePointAsync(safe, collection, ct);

        var act = () => safe.EnsureCollectionAsync(collection, 16, ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AllowDestructiveDimensionMigration*");

        (await safe.CountAsync(collection, ct)).Should().Be(1,
            "izin verilmediğinde mevcut veri KORUNMALI");
    }

    /// <summary>Karşı yön: açık izinle koleksiyon yeniden oluşturulur (veri gider — beklenen).</summary>
    [Fact]
    public async Task EnsureCollection_DimensionMismatchWithPermission_RecreatesEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var collection = NewCollection();

        await Adapter(allowDestructive: false).EnsureCollectionAsync(collection, 8, ct);
        await SeedOnePointAsync(Adapter(false), collection, ct);

        await Adapter(allowDestructive: true).EnsureCollectionAsync(collection, 16, ct);

        (await Adapter(true).CountAsync(collection, ct)).Should().Be(0,
            "izin açıkken koleksiyon sıfırdan oluşturulur");
    }

    private static Task SeedOnePointAsync(
        QdrantVectorMemoryAdapter adapter, string collection, CancellationToken ct) =>
        adapter.UpsertAsync(
            collection,
            [(new MemoryDocument
                {
                    Id = "d1",
                    Kind = MemoryKind.Knowledge,
                    Title = "test",
                    Text = "iade politikası"
                },
                new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f })],
            ct);

    // ─── Kişisel veri silme ──────────────────────────────────────────────────

    private static (MemoryDocument Doc, float[] Vector) Episode(string sessionId, string? customerId, int seed)
    {
        var doc = new MemoryDocument { Kind = MemoryKind.Episodic, SessionId = sessionId, Text = $"soru {seed}" };
        if (customerId is not null) doc.Tags["customerId"] = customerId;
        var v = new float[8];
        v[seed % 8] = 1f;
        return (doc, v);
    }

    [Fact]
    public async Task DeleteBySessions_RemovesOnlyThoseSessionsDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        var collection = NewCollection();
        var adapter = Adapter(false);
        await adapter.EnsureCollectionAsync(collection, 8, ct);
        await adapter.UpsertAsync(collection, [Episode("s1", "1001", 1), Episode("s2", "1001", 2), Episode("s3", "2002", 3)], ct);

        await adapter.DeleteBySessionsAsync(collection, ["s1", "s2"], ct);

        (await adapter.CountAsync(collection, ct)).Should().Be(1);
        var left = await adapter.SearchAsync(collection, Episode("x", null, 3).Vector, 10, 0f, null, ct);
        left.Should().ContainSingle().Which.Document.SessionId.Should().Be("s3");
    }

    [Fact]
    public async Task DeleteWhereTag_RemovesOnlyMatchingDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        var collection = NewCollection();
        var adapter = Adapter(false);
        await adapter.EnsureCollectionAsync(collection, 8, ct);
        await adapter.UpsertAsync(collection, [Episode("s1", "1001", 1), Episode("s2", "2002", 2), Episode("s3", null, 3)], ct);

        await adapter.DeleteWhereTagAsync(collection, "customerId", "1001", ct);

        (await adapter.CountAsync(collection, ct)).Should().Be(2);
        var left = await adapter.SearchAsync(collection, Episode("x", null, 1).Vector, 10, -1f, null, ct);
        left.Select(h => h.Document.SessionId).Should().BeEquivalentTo(["s2", "s3"]);
    }
}
