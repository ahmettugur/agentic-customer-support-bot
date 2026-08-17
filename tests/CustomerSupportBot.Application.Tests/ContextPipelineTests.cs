// Tests/Services/ContextPipelineTests.cs
//
// Bağlam kurulumunun üç koruması: zaman aşımı, kritik/iyileştirici ayrımı, bütçe.
//
// Ortak tema: bağlam bir İYİLEŞTİRMEDİR, zorunluluk değil. Üretilemediğinde tur devam
// etmeli — ama model, neyi göremediğini biliyor olmalı. Bu ayrımın yokluğu gerçek bir hata
// sınıfı üretiyordu: altyapı arızası (provider hata verdi/yavaşladı) kullanıcıya YANLIŞ OLGU
// olarak yansıyordu ("kayıtlı siparişiniz bulunamadı").

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Application.Services.Providers;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ContextPipelineTests
{
    private static AgentSession Session() => new() { SessionId = "s1" };

    private static ContextPipeline Build(
        IEnumerable<IContextProvider> providers, ContextPipelineOptions? options = null) =>
        new(providers, Options.Create(options ?? new ContextPipelineOptions()),
            NullLogger<ContextPipeline>.Instance);

    /// <summary>Yapılandırılabilir sahte provider.</summary>
    private sealed class FakeProvider(
        string name, int order, string? text,
        bool critical = false, Exception? throws = null, TimeSpan? delay = null) : IContextProvider
    {
        public string Name => name;
        public int Order => order;
        public bool IsCritical => critical;

        public async Task<string?> GetContextAsync(AgentSession session, string q, CancellationToken ct = default)
        {
            if (delay is { } d) await Task.Delay(d, ct);
            if (throws is not null) throw throws;
            return text;
        }
    }

    // ═══ Temel birleştirme ═══

    [Fact]
    public async Task Providers_AreJoinedInOrderRegardlessOfCompletion()
    {
        // Order 1 kasıtlı olarak YAVAŞ: paralel koştukları için 2 önce biter, ama çıktı
        // sırası Order'a göre deterministik kalmalı.
        var pipeline = Build([
            new FakeProvider("Slow", 1, "BİRİNCİ", delay: TimeSpan.FromMilliseconds(80)),
            new FakeProvider("Fast", 2, "İKİNCİ")
        ]);

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Text.IndexOf("BİRİNCİ", StringComparison.Ordinal)
            .Should().BeLessThan(result.Text.IndexOf("İKİNCİ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmptyProvider_IsRecordedButNotIncluded()
    {
        var pipeline = Build([new FakeProvider("Bos", 1, null)]);

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Text.Should().BeEmpty();
        result.Included("Bos").Should().BeFalse();
        result.Parts.Single().Status.Should().Be(ContextPartStatus.Empty);
    }

    // ═══ 1. Zaman aşımı ═══

    /// <summary>
    /// Pipeline içinde ağ ve LLM çağrıları var (semantik arama, özetleme). Zaman aşımı
    /// olmadan yavaş bir provider turu belirsiz süre bloklardı — üstelik WorkflowGuard
    /// timeout'u bağlam kurulumundan SONRA devreye giriyor.
    /// </summary>
    [Fact]
    public async Task SlowProvider_TimesOut_AndTurnContinues()
    {
        var pipeline = Build(
            [
                new FakeProvider("Yavas", 1, "gelmeyecek", delay: TimeSpan.FromSeconds(30)),
                new FakeProvider("Hizli", 2, "HIZLI")
            ],
            new ContextPipelineOptions { ProviderTimeoutSeconds = 1 });

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Parts.Single(p => p.ProviderName == "Yavas").Status
            .Should().Be(ContextPartStatus.TimedOut);
        result.Text.Should().Contain("HIZLI", "yavaş provider hızlı olanı engellememeli");
    }

    [Fact]
    public async Task FailingProvider_IsIsolated()
    {
        var pipeline = Build([
            new FakeProvider("Patlayan", 1, null, throws: new InvalidOperationException("db down")),
            new FakeProvider("Saglam", 2, "SAĞLAM")
        ]);

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Parts.Single(p => p.ProviderName == "Patlayan").Status
            .Should().Be(ContextPartStatus.Failed);
        result.Text.Should().Contain("SAĞLAM");
    }

    // ═══ 2. Kritik / iyileştirici ayrımı ═══

    /// <summary>
    /// İyileştirici provider düşerse sessizce atlanır — bot yine makul cevap verebilir.
    /// </summary>
    [Fact]
    public async Task EnhancingProvider_Fails_Silently()
    {
        var pipeline = Build([
            new FakeProvider("Semantik", 1, null, critical: false, throws: new Exception("boom"))
        ]);

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Text.Should().BeEmpty("iyileştirici provider için sessiz atlama doğrudur");
    }

    /// <summary>
    /// Kritik provider düşerse model UYARILMALI. Sessiz atlama, altyapı hatasını kullanıcıya
    /// yanlış olgu olarak yansıtırdı — bu testin koruduğu şey tam olarak o.
    /// </summary>
    [Theory]
    [InlineData(false)]  // hata
    [InlineData(true)]   // zaman aşımı
    public async Task CriticalProvider_Degrades_WarnsTheModel(bool viaTimeout)
    {
        var provider = viaTimeout
            ? new FakeProvider("MusteriBaglami", 1, "x", critical: true, delay: TimeSpan.FromSeconds(30))
            : new FakeProvider("MusteriBaglami", 1, null, critical: true, throws: new Exception("db down"));

        var pipeline = Build([provider], new ContextPipelineOptions { ProviderTimeoutSeconds = 1 });

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Text.Should().Contain("UYARI");
        result.Text.Should().Contain("MusteriBaglami");
        result.Text.Should().Contain("tahminde bulunma");
    }

    // ═══ 3. Bütçe ═══

    [Fact]
    public async Task OversizedProvider_IsTruncated()
    {
        var pipeline = Build(
            [new FakeProvider("Uzun", 1, new string('x', 5000))],
            new ContextPipelineOptions { MaxProviderChars = 100, MaxTotalChars = 1000 });

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Text.Length.Should().BeLessThan(200);
        result.Text.Should().Contain("kırpıldı");
    }

    /// <summary>
    /// Tavana ulaşıldığında DÜŞÜK öncelikli (yüksek Order) olan dışarıda kalmalı — kritik
    /// bağlam önce yerleşir.
    /// </summary>
    [Fact]
    public async Task WhenBudgetIsFull_LowestPriorityIsDropped()
    {
        var pipeline = Build(
            [
                new FakeProvider("Onemli", 1, new string('a', 400)),
                new FakeProvider("Ikincil", 99, new string('b', 400))
            ],
            new ContextPipelineOptions { MaxProviderChars = 500, MaxTotalChars = 500 });

        var result = await pipeline.BuildContextAsync(Session(), "q");

        result.Included("Onemli").Should().BeTrue();
        result.Parts.Single(p => p.ProviderName == "Ikincil").Status
            .Should().Be(ContextPartStatus.Dropped);
    }

    // ═══ İptal ═══

    /// <summary>
    /// Çağıran turu iptal ettiyse bu provider'ın arızası değildir — "başarısız" diye
    /// raporlanmamalı, gürültü yapmamalı.
    /// </summary>
    [Fact]
    public async Task CallerCancellation_IsNotReportedAsProviderFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var pipeline = Build([
            new FakeProvider("Herhangi", 1, "x", delay: TimeSpan.FromSeconds(5))
        ]);

        var result = await pipeline.BuildContextAsync(Session(), "q", cts.Token);

        result.Parts.Single().Status.Should().Be(ContextPartStatus.Empty);
    }
}
