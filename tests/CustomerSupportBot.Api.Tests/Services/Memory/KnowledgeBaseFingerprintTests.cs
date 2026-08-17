// KnowledgeBase ingest'inin değişiklik parmakizi.
//
// Parmakizi yanlışsa ingest sessizce atlanır: makale değişir ama indeks eski kalır.
// Reflection KULLANILMAZ — ComputeArticlesFingerprint internal, InternalsVisibleTo ile çağrılıyor.

using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model.Memory;

namespace CustomerSupportBot.Api.Tests.Services.Memory;

public class KnowledgeBaseFingerprintTests
{
    private static KnowledgeArticle Article(string id, long ticks) => new()
    {
        Id = id,
        UpdatedAt = new DateTime(ticks, DateTimeKind.Utc)
    };

    [Fact]
    public void Fingerprint_NoArticles_IsStableSentinel()
    {
        KnowledgeBaseIngestionService.ComputeArticlesFingerprint([])
            .Should().Be(KnowledgeBaseIngestionService.ComputeArticlesFingerprint([]));
    }

    [Fact]
    public void Fingerprint_IsIndependentOfStoreOrdering()
    {
        var a = Article("a", 1_000);
        var b = Article("b", 2_000);

        KnowledgeBaseIngestionService.ComputeArticlesFingerprint([a, b])
            .Should().Be(KnowledgeBaseIngestionService.ComputeArticlesFingerprint([b, a]),
                "store'un dönüş sırası değiştiği için gereksiz re-ingest tetiklenmemeli");
    }

    [Fact]
    public void Fingerprint_ChangesWhenArticleIsEdited()
    {
        var before = KnowledgeBaseIngestionService.ComputeArticlesFingerprint([Article("a", 1_000)]);
        var after = KnowledgeBaseIngestionService.ComputeArticlesFingerprint([Article("a", 2_000)]);

        after.Should().NotBe(before);
    }

    [Fact]
    public void Fingerprint_ChangesWhenArticleIsAddedOrRemoved()
    {
        var one = KnowledgeBaseIngestionService.ComputeArticlesFingerprint([Article("a", 1_000)]);
        var two = KnowledgeBaseIngestionService.ComputeArticlesFingerprint([Article("a", 1_000), Article("b", 1_000)]);

        two.Should().NotBe(one);
        one.Should().NotBe(KnowledgeBaseIngestionService.ComputeArticlesFingerprint([]));
    }

    [Fact]
    public void ChunkId_IsDeterministicAndScopedToArticle()
    {
        KnowledgeArticle.ChunkId("a1", 2).Should().Be(KnowledgeArticle.ChunkId("a1", 2));
        KnowledgeArticle.ChunkId("a1", 2).Should().NotBe(KnowledgeArticle.ChunkId("a1", 3));
        KnowledgeArticle.ChunkId("a1", 2).Should().NotBe(KnowledgeArticle.ChunkId("a2", 2));
    }
}
