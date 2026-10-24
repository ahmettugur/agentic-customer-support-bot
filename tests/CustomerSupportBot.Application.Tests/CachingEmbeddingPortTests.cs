// Sorgu embedding'i önbelleği: aynı soru kısa sürede tekrar sorulduğunda embedding API'sine tekrar gidilmez.
// (Yanıt önbelleği DEĞİL — yanıtlar canlı veriye ve müşteriye bağlıdır; yalnızca saf, deterministik vektör.)

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Memory;

namespace CustomerSupportBot.Application.Tests;

public class CachingEmbeddingPortTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class Counting : IEmbeddingPort
    {
        public int Calls { get; private set; }
        public int BatchCalls { get; private set; }
        public bool FailNext { get; set; }
        public int Dimension => 3;
        public bool IsConfigured => true;

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            Calls++;
            if (FailNext) { FailNext = false; throw new InvalidOperationException("api"); }
            return Task.FromResult(new float[] { text.Length, 1, 2 });
        }

        public Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            BatchCalls++;
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => new float[] { t.Length, 1, 2 }).ToList());
        }
    }

    private static (CachingEmbeddingPort Sut, Counting Inner, FakeTimeProvider Clock) Build(int size = 100, int minutes = 60)
    {
        var inner = new Counting();
        var clock = new FakeTimeProvider(Now);
        return (new CachingEmbeddingPort(inner, size, TimeSpan.FromMinutes(minutes), clock), inner, clock);
    }

    [Fact]
    public async Task TheSameQuery_IsEmbeddedOnce()
    {
        var (sut, inner, _) = Build();

        var a = await sut.EmbedAsync("iade süresi nedir", Ct);
        var b = await sut.EmbedAsync("iade süresi nedir", Ct);

        inner.Calls.Should().Be(1);
        b.Should().Equal(a);
    }

    [Fact]
    public async Task SurroundingWhitespace_IsIgnored_ButCaseIsNot()
    {
        var (sut, inner, _) = Build();

        await sut.EmbedAsync("merhaba", Ct);
        await sut.EmbedAsync("  merhaba  ", Ct);
        await sut.EmbedAsync("Merhaba", Ct);

        inner.Calls.Should().Be(2, "büyük/küçük harf farklı bir vektör verebilir; yalnızca boşluk yok sayılır");
    }

    [Fact]
    public async Task ACachedVector_CannotBeCorruptedByTheCaller()
    {
        var (sut, _, _) = Build();
        var first = await sut.EmbedAsync("x", Ct);
        first[0] = 999;

        (await sut.EmbedAsync("x", Ct))[0].Should().Be(1);
    }

    [Fact]
    public async Task EntriesExpire()
    {
        var (sut, inner, clock) = Build(minutes: 10);

        await sut.EmbedAsync("x", Ct);
        clock.Advance(TimeSpan.FromMinutes(11));
        await sut.EmbedAsync("x", Ct);

        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task TheCache_IsBounded_AndEvictsTheOldest()
    {
        var (sut, inner, clock) = Build(size: 2);

        await sut.EmbedAsync("a", Ct); clock.Advance(TimeSpan.FromSeconds(1));
        await sut.EmbedAsync("b", Ct); clock.Advance(TimeSpan.FromSeconds(1));
        await sut.EmbedAsync("c", Ct);          // a düşer
        await sut.EmbedAsync("a", Ct);

        inner.Calls.Should().Be(4);
    }

    [Fact]
    public async Task AFailure_IsNotCached()
    {
        var (sut, inner, _) = Build();
        inner.FailNext = true;

        await sut.Invoking(s => s.EmbedAsync("x", Ct)).Should().ThrowAsync<InvalidOperationException>();
        await sut.EmbedAsync("x", Ct);

        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task BatchIngestion_BypassesTheCache()
    {
        var (sut, inner, _) = Build();

        await sut.EmbedBatchAsync(["a", "b"], Ct);
        await sut.EmbedBatchAsync(["a", "b"], Ct);

        inner.BatchCalls.Should().Be(2);
        inner.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ASizeOfZero_DisablesCaching()
    {
        var (sut, inner, _) = Build(size: 0);

        await sut.EmbedAsync("x", Ct);
        await sut.EmbedAsync("x", Ct);

        inner.Calls.Should().Be(2);
    }

    [Fact]
    public void DimensionAndConfiguration_PassThrough()
    {
        var (sut, _, _) = Build();

        sut.Dimension.Should().Be(3);
        sut.IsConfigured.Should().BeTrue();
    }
}
