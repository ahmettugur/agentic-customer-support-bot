// Embedding boyutu değiştiğinde koleksiyonun akıbeti.
//
// Silme eskiden koşulsuzdu ve "dev-friendly" diye işaretlenmişti; ortam ayrımı yoktu, yani
// üretimde de çalışıyordu. Embedding modelini değiştirmek (ör. -small → -large) knowledge
// base'i, dersleri ve episodic belleği sessizce siliyordu — episodic ve dersler yeniden
// üretilemez.

using CustomerSupportBot.Adapters.AI.Qdrant;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class QdrantDimensionGuardTests
{
    private const string Collection = "cs_knowledge";

    [Fact]
    public void MatchingDimension_NeedsNoRecreate()
    {
        QdrantVectorMemoryAdapter.RequiresRecreate(Collection, existingDim: 1536, expectedDim: 1536,
            allowDestructive: false).Should().BeFalse();
    }

    /// <summary>
    /// Boyut bilinemiyorsa (0) veri silinmez — belirsizlik, yıkıcı işlem için gerekçe değildir.
    /// </summary>
    [Fact]
    public void UnknownDimension_NeedsNoRecreate()
    {
        QdrantVectorMemoryAdapter.RequiresRecreate(Collection, existingDim: 0, expectedDim: 1536,
            allowDestructive: false).Should().BeFalse();
    }

    /// <summary>
    /// Uyuşmazlık + izin YOK → fail-closed. Varsayılan bu olmalı: veri kaybı sessiz bir yan
    /// etki değil, bilinçli bir karar olmalıdır.
    /// </summary>
    [Fact]
    public void MismatchWithoutPermission_Throws()
    {
        var act = () => QdrantVectorMemoryAdapter.RequiresRecreate(
            Collection, existingDim: 1536, expectedDim: 3072, allowDestructive: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AllowDestructiveDimensionMigration*",
                "hata, operatöre hangi anahtarın açılması gerektiğini söylemeli");
    }

    /// <summary>Uyuşmazlık + açık izin → yeniden oluşturma. Bayrağın bir işe yaraması gerekir.</summary>
    [Fact]
    public void MismatchWithExplicitPermission_Recreates()
    {
        QdrantVectorMemoryAdapter.RequiresRecreate(Collection, existingDim: 1536, expectedDim: 3072,
            allowDestructive: true).Should().BeTrue();
    }
}
