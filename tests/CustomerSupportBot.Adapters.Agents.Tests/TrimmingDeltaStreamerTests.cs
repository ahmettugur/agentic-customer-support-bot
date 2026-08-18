// Tests/TrimmingDeltaStreamerTests.cs
//
// Tek değişmez: parça parça beslenen metnin yayınlanan hâli, girdinin Trim()'lenmiş hâline
// BİREBİR eşit olmalı — parçaların nerede bölündüğünden bağımsız olarak.
//
// Bu neden önemli: DecomposedRunner sıralı alt görevlerde gerçek LLM token akışını canlı
// iletir, ama alt görevin nihai metni FormatSubTaskResult içinde Trim()'lenir. Bu bileşen
// olmadan akan metin ile response_complete'teki nihai metin baştaki/sondaki boşluk kadar
// ayrışır ve ilerlemeli yayının en güçlü güvencesi (birleşimin nihai metne eşitliği) bozulur.

using CustomerSupportBot.Adapters.Agents;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class TrimmingDeltaStreamerTests
{
    private static string StreamAll(string input, int chunkSize)
    {
        var streamer = new TrimmingDeltaStreamer();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i += chunkSize)
            sb.Append(streamer.Feed(input.Substring(i, Math.Min(chunkSize, input.Length - i))));
        return sb.ToString();
    }

    /// <summary>
    /// Asıl değişmez, her parça boyutunda: sonuç == input.Trim(). Parça boyutunu değiştirmek
    /// gerçek LLM akışındaki rastgele token sınırlarını taklit eder.
    /// </summary>
    [Theory]
    [InlineData("düz metin")]
    [InlineData("   baştan boşluk")]
    [InlineData("sondan boşluk   ")]
    [InlineData("   iki taraf   ")]
    [InlineData("\n\n satır sonları \n\n")]
    [InlineData("iç   boşluklar   korunur")]
    [InlineData("araya\nsatır\nsonu")]
    [InlineData("tek")]
    [InlineData("")]
    [InlineData("     ")]
    [InlineData("\t\n \r\n ")]
    public void Streamed_EqualsTrim_ForEveryChunkSize(string input)
    {
        for (int chunkSize = 1; chunkSize <= Math.Max(1, input.Length); chunkSize++)
        {
            StreamAll(input, chunkSize).Should().Be(input.Trim(),
                $"parça boyutu {chunkSize} için akan metin Trim() ile aynı olmalı");
        }
    }

    /// <summary>
    /// İç boşluklar KAYBOLMAMALI — yalnızca baştaki ve sondaki atılır. Bu ayrım olmadan
    /// bileşen "boşlukları at" gibi davranır ve metin bozulurdu.
    /// </summary>
    [Fact]
    public void PreservesInteriorWhitespace()
    {
        StreamAll("a   b\n\nc", chunkSize: 1).Should().Be("a   b\n\nc");
    }

    /// <summary>
    /// Sondaki boşluk, arkasından içerik gelene kadar TUTULUR; gelirse yayınlanır.
    /// Erken yayınlansaydı akış sonunda fazladan boşluk kalırdı.
    /// </summary>
    [Fact]
    public void HeldWhitespace_IsEmittedOnlyWhenMoreContentFollows()
    {
        var s = new TrimmingDeltaStreamer();

        s.Feed("abc").Should().Be("abc");
        s.Feed("   ").Should().BeEmpty("bu boşluk sondaki olabilir — henüz yayınlanmamalı");
        s.Feed("def").Should().Be("   def", "içerik gelince tutulan boşluk önce yayınlanır");
        s.Feed("  ").Should().BeEmpty("akış burada biterse bu boşluk hiç yayınlanmaz");
    }

    /// <summary>Baştaki boşluk hiçbir zaman yayınlanmaz.</summary>
    [Fact]
    public void LeadingWhitespace_IsNeverEmitted()
    {
        var s = new TrimmingDeltaStreamer();

        s.Feed("   ").Should().BeEmpty();
        s.Feed("\n\t").Should().BeEmpty();
        s.Feed("x").Should().Be("x");
    }

    [Fact]
    public void NullOrEmptyChunk_IsSafe()
    {
        var s = new TrimmingDeltaStreamer();
        s.Feed(null).Should().BeEmpty();
        s.Feed("").Should().BeEmpty();
        s.Feed("a").Should().Be("a");
    }
}
