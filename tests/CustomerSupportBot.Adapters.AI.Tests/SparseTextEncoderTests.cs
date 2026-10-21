// Hibrit aramanın sözcüksel (lexical) tarafı: metin → Qdrant sparse vektörü.
// Türkçe için kritik davranışlar: büyük/küçük "İ/I" doğru indirgenir, aksan düşer (müşteriler "iade sureci"
// yazar), ekler kabaca kırpılır ("kargom" ≈ "kargo"), sayılar (sipariş no) olduğu gibi kalır.

using CustomerSupportBot.Adapters.AI.Qdrant;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class SparseTextEncoderTests
{
    [Theory]
    [InlineData("İade", "iade")]
    [InlineData("IADE", "iade")]
    [InlineData("Çözüm", "cozum")]
    [InlineData("ŞİKAYET", "sikay")]   // aksan düşer, 5 karaktere kırpılır
    public void Tokenize_FoldsTurkishCaseAndDiacritics(string input, string expected)
    {
        Assert.Equal([expected], SparseTextEncoder.Tokenize(input));
    }

    [Fact]
    public void Tokenize_TruncatesLongWords_SoInflectedFormsMatch()
    {
        Assert.Equal(SparseTextEncoder.Tokenize("kargo"), SparseTextEncoder.Tokenize("kargom"));
        Assert.Equal(SparseTextEncoder.Tokenize("kargoyu"), SparseTextEncoder.Tokenize("kargolar"));
    }

    [Fact]
    public void Tokenize_KeepsNumbersWhole()
    {
        var tokens = SparseTextEncoder.Tokenize("sipariş 1027384 iptal");
        Assert.Contains("1027384", tokens);
    }

    [Fact]
    public void Tokenize_DropsStopwordsAndSingleCharacters()
    {
        Assert.Equal(["kargo"], SparseTextEncoder.Tokenize("bir kargo ve a"));
    }

    [Fact]
    public void Tokenize_SplitsOnPunctuation()
    {
        Assert.Equal(["iade", "taleb"], SparseTextEncoder.Tokenize("iade-talebi!"));
    }

    [Fact]
    public void EncodeDocument_RepeatedTermsWeighMoreButSublinearly()
    {
        var once = SparseTextEncoder.EncodeDocument("kargo");
        var thrice = SparseTextEncoder.EncodeDocument("kargo kargo kargo");

        Assert.Single(once.Indices);
        Assert.Single(thrice.Indices);
        Assert.Equal(once.Indices[0], thrice.Indices[0]);
        Assert.True(thrice.Values[0] > once.Values[0]);
        Assert.True(thrice.Values[0] < 3 * once.Values[0]);
    }

    [Fact]
    public void EncodeDocument_ProducesUniqueIndices()
    {
        var v = SparseTextEncoder.EncodeDocument("kargo iade kargo sipariş iade teslimat");

        Assert.Equal(v.Indices.Length, v.Indices.Distinct().Count());
        Assert.Equal(v.Indices.Length, v.Values.Length);
    }

    [Fact]
    public void EncodeQuery_GivesEachDistinctTermWeightOne()
    {
        var v = SparseTextEncoder.EncodeQuery("kargo kargo iade");

        Assert.Equal(2, v.Indices.Length);
        Assert.All(v.Values, w => Assert.Equal(1f, w));
    }

    [Fact]
    public void DocumentAndQuery_ShareTheSameIndexForTheSameTerm()
    {
        var doc = SparseTextEncoder.EncodeDocument("Kargom nerede kaldı");
        var query = SparseTextEncoder.EncodeQuery("KARGO");

        Assert.Contains(query.Indices[0], doc.Indices);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ve bir bu")]
    public void Encode_WithNoUsableTerms_ReturnsEmpty(string text)
    {
        Assert.Empty(SparseTextEncoder.EncodeDocument(text).Indices);
        Assert.Empty(SparseTextEncoder.EncodeQuery(text).Indices);
    }
}
