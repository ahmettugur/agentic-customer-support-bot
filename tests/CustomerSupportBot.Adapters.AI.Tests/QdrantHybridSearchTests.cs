// Hibrit arama (dense + sözcüksel) — GERÇEK Qdrant'a karşı.
//
// Dense arama anlamı yakalar ama tam eşleşme gereken sorgularda (ürün/iade kodu, sipariş no) zayıftır:
// kodun embedding'i ilgili belgeye yakın çıkmayabilir. Sözcüksel sinyal bu boşluğu kapatır. Burada ölçülen:
// dense'in KAÇIRDIĞI belgeyi hibrit buluyor mu, etiket süzmesi sözcüksel taraf için de geçerli mi, mevcut
// (hibritten önce yazılmış) koleksiyon backfill ediliyor mu ve — KVKK için — silmeler sözcüksel kopyayı da siliyor mu.

using CustomerSupportBot.Adapters.AI.Qdrant;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace CustomerSupportBot.Adapters.AI.Tests;

[Collection("Qdrant")]
public class QdrantHybridSearchTests : IClassFixture<QdrantFixture>
{
    private const int Dim = 4;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly QdrantFixture _fixture;
    public QdrantHybridSearchTests(QdrantFixture fixture) => _fixture = fixture;

    private QdrantVectorMemoryAdapter Adapter(bool allowDestructive = false) =>
        new(Options.Create(new SemanticMemoryOptions
        {
            VectorStore = new SemanticMemoryOptions.VectorStoreOptions
            {
                Host = _fixture.Host, Port = _fixture.GrpcPort, UseHttps = false,
                AllowDestructiveDimensionMigration = allowDestructive
            }
        }), NullLogger<QdrantVectorMemoryAdapter>.Instance);

    private static string NewCollection() => $"h_{Guid.NewGuid():N}";

    private static (MemoryDocument Doc, float[] Vector) Doc(
        string id, string text, float[] vector, string? session = null, string? customer = null)
    {
        var doc = new MemoryDocument { Id = id, Kind = MemoryKind.Knowledge, Title = id, Text = text, SessionId = session };
        if (customer != null) doc.Tags["customerId"] = customer;
        return (doc, vector);
    }

    private static readonly float[] AxisA = [1, 0, 0, 0];
    private static readonly float[] AxisB = [0, 1, 0, 0];
    private static readonly float[] AxisC = [0, 0, 1, 0];

    private async Task<(QdrantVectorMemoryAdapter Adapter, string Collection)> SeededAsync()
    {
        var adapter = Adapter();
        var collection = NewCollection();
        await adapter.EnsureCollectionAsync(collection, Dim, Ct);
        await adapter.UpsertAsync(collection,
        [
            Doc("kargo", "Kargo takip numarasını sipariş detay sayfasından öğrenebilirsiniz.", AxisA),
            Doc("kod", "İade kodu XK-9921 geçersiz görünüyorsa müşteri hizmetleriyle iletişime geçin.", AxisB),
            Doc("garanti", "Ürünlerde garanti süresi iki yıldır.", AxisC),
        ], Ct);
        return (adapter, collection);
    }

    [Fact]
    public async Task HybridSearch_FindsAnExactCodeThatDenseSearchMisses()
    {
        var (adapter, collection) = await SeededAsync();
        // Sorgu vektörü A/C eksenine yakın, "kod" belgesine (B ekseni) dik: dense bu belgeyi eşik altı bırakır.
        float[] queryVector = [0.9f, 0f, 0.1f, 0f];

        var dense = await adapter.SearchAsync(collection, queryVector, 5, 0.35f, null, Ct);
        var hybrid = await adapter.HybridSearchAsync(collection, queryVector, "XK-9921 kodu geçersiz", 5, 0.35f, null, Ct);

        Assert.DoesNotContain(dense, h => h.Document.Id == "kod");
        Assert.Contains(hybrid, h => h.Document.Id == "kod");
    }

    [Fact]
    public async Task HybridSearch_StillReturnsDenseMatches_AndRanksDoubleMatchFirst()
    {
        var (adapter, collection) = await SeededAsync();

        // Hem anlamca (A ekseni) hem sözcükçe ("kargo takip") "kargo" belgesine uyan sorgu.
        var hybrid = await adapter.HybridSearchAsync(collection, AxisA, "kargo takip numarası", 5, 0.35f, null, Ct);

        Assert.Equal("kargo", hybrid[0].Document.Id);
        Assert.Equal(1.0, hybrid[0].Score, precision: 3);   // her iki sinyalde 1. sıra = ölçeklenmiş tavan skor
    }

    [Fact]
    public async Task HybridSearch_AppliesTagFilterToTheLexicalSignalToo()
    {
        var adapter = Adapter();
        var collection = NewCollection();
        await adapter.EnsureCollectionAsync(collection, Dim, Ct);
        await adapter.UpsertAsync(collection,
        [
            Doc("ayse", "Sipariş 5551 için kargo gecikti.", AxisA, customer: "A"),
            Doc("mehmet", "Sipariş 5551 için kargo gecikti.", AxisB, customer: "B"),
        ], Ct);

        var hits = await adapter.HybridSearchAsync(collection, AxisC, "5551 kargo", 5, 0.35f,
            new Dictionary<string, string> { ["customerId"] = "B" }, Ct);

        Assert.Equal(["mehmet"], hits.Select(h => h.Document.Id));
    }

    [Fact]
    public async Task HybridSearch_WithNoUsableQueryTerms_FallsBackToDenseOnly()
    {
        var (adapter, collection) = await SeededAsync();

        var hits = await adapter.HybridSearchAsync(collection, AxisC, "ve bir bu", 5, 0.35f, null, Ct);

        Assert.Equal(["garanti"], hits.Select(h => h.Document.Id));
    }

    [Fact]
    public async Task EnsureCollection_BackfillsLexicalIndexForACollectionWrittenBeforeHybrid()
    {
        // Hibritten ÖNCE yazılmış koleksiyon: yalnızca dense vektör var, yan (sözcüksel) koleksiyon yok.
        var collection = NewCollection();
        var raw = new QdrantClient(_fixture.Host, _fixture.GrpcPort, false);
        await raw.CreateCollectionAsync(collection, new VectorParams { Size = Dim, Distance = Distance.Cosine }, cancellationToken: Ct);
        var point = new PointStruct { Id = AdapterIds.Guid("eski-kod"), Vectors = AxisB };
        point.Payload["_docId"] = "eski-kod";
        point.Payload["_kind"] = "Knowledge";
        point.Payload["_title"] = "Eski";
        point.Payload["_text"] = "Eski iade kodu ZQ-4471 hakkında bilgi.";
        await raw.UpsertAsync(collection, [point], cancellationToken: Ct);

        var adapter = Adapter();
        await adapter.EnsureCollectionAsync(collection, Dim, Ct);   // yan koleksiyonu kurar ve doldurur

        var hits = await adapter.HybridSearchAsync(collection, AxisA, "ZQ-4471", 5, 0.35f, null, Ct);
        Assert.Contains(hits, h => h.Document.Id == "eski-kod");
    }

    [Fact]
    public async Task Delete_RemovesTheDocumentFromTheLexicalIndexToo()
    {
        var (adapter, collection) = await SeededAsync();

        await adapter.DeleteAsync(collection, "kod", Ct);

        var hits = await adapter.HybridSearchAsync(collection, AxisC, "XK-9921", 5, 0.35f, null, Ct);
        Assert.DoesNotContain(hits, h => h.Document.Id == "kod");
    }

    [Fact]
    public async Task EraseBySessionAndByCustomer_AlsoRemoveTheLexicalCopy()
    {
        // Sözcüksel koleksiyon belge metnini kopyalar: KVKK silme onu da kapsamalı, yoksa silinen veri aranabilir kalır.
        var adapter = Adapter();
        var collection = NewCollection();
        await adapter.EnsureCollectionAsync(collection, Dim, Ct);
        await adapter.UpsertAsync(collection,
        [
            Doc("e1", "Müşteri 7788 numaralı siparişi için şikayet etti.", AxisA, session: "s1", customer: "C1"),
            Doc("e2", "Müşteri 9911 numaralı siparişi için şikayet etti.", AxisB, session: "s2", customer: "C2"),
        ], Ct);

        await adapter.DeleteBySessionsAsync(collection, ["s1"], Ct);
        await adapter.DeleteWhereTagAsync(collection, "customerId", "C2", Ct);

        Assert.Empty(await adapter.HybridSearchAsync(collection, AxisC, "7788", 5, 0.35f, null, Ct));
        Assert.Empty(await adapter.HybridSearchAsync(collection, AxisC, "9911", 5, 0.35f, null, Ct));
    }

    [Fact]
    public async Task DestructiveRecreate_AlsoResetsTheLexicalIndex()
    {
        var (adapter, collection) = await SeededAsync();

        // Boyut değişimi izinle koleksiyonu yeniden kurar; eski belgeler yan koleksiyonda kalmamalı.
        await Adapter(allowDestructive: true).EnsureCollectionAsync(collection, Dim + 1, Ct);

        var hits = await adapter.HybridSearchAsync(collection, [1, 0, 0, 0, 0], "XK-9921", 5, 0.35f, null, Ct);
        Assert.Empty(hits);
    }
}

internal static class AdapterIds
{
    public static Guid Guid(string id) => QdrantVectorMemoryAdapter.ToPointGuid(id);
}
