// Tests/Services/SessionStateServiceConcurrencyTests.cs
// Turun türetilmiş state'i (intent, sentiment, ConsecutiveNegativeTurns) TEK yazardan
// geçer: SessionStateExtractor.ExtractAndApply, session manager tarafından kilit altında
// çağrılır. Buradaki testler o tek yazarın eşzamanlılık davranışını ve alarm eşiğini
// doğrular.
//
// Eskiden bu alanların ikinci bir yazarı vardı (SessionStateService.UpdateSessionSentiment /
// UpdateSessionIntentAsync, ChatPortService'ten tur ortasında çağrılıyordu). O yol
// kaldırıldı; sebebi ve sonuçları için bkz. TurnSignals.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Application.Tests;

public class SessionStateServiceConcurrencyTests
{
    private static InMemorySessionManager BuildManager() =>
        new(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));

    private static SessionStateService BuildService() =>
        new(Substitute.For<ISessionManager>(), NullLogger<SessionStateService>.Instance);

    /// <summary>NegativeThreshold (0.35) altında bir LLM sinyali.</summary>
    private static TurnSignals NegativeSignal() =>
        new(null, WellKnown.Sentiments.Negative, 0.1);

    [Fact]
    public async Task AddExchange_ConcurrentCallsSameSession_NoLostIncrements()
    {
        var mgr = BuildManager();
        var ct = TestContext.Current.CancellationToken;
        await mgr.GetOrCreateAsync("s1", ct);

        const int concurrentCalls = 50;
        var tasks = Enumerable.Range(0, concurrentCalls)
            .Select(i => mgr.AddExchangeAsync("s1", $"mesaj {i}", "yanıt", NegativeSignal(), ct));

        await Task.WhenAll(tasks);

        // Kilit olmadan (oku-değiştir-yaz yarışı) bu sayı 50'den küçük çıkardı —
        // eşzamanlı iki çağrı aynı değeri okuyup aynı sonucu yazınca bir artış kaybolur.
        var session = await mgr.GetOrCreateAsync("s1", ct);
        session.State.ConsecutiveNegativeTurns.Should().Be(concurrentCalls);
    }

    [Fact]
    public async Task AddExchange_OneTurn_IncrementsCounterExactlyOnce()
    {
        // ÇİFT SAYIM REGRESYON TESTİ. Turun türetilmiş alanlarının ikinci bir yazarı
        // olduğunda sayaç tur başına 2 ilerliyordu ve otomatik eskalasyon eşiği (3)
        // 3 tur yerine 2 turda aşılıyordu.
        var mgr = BuildManager();
        var ct = TestContext.Current.CancellationToken;

        await mgr.AddExchangeAsync("s1", "siparişim gelmedi", "üzgünüm", NegativeSignal(), ct);
        var session = await mgr.GetOrCreateAsync("s1", ct);
        session.State.ConsecutiveNegativeTurns.Should().Be(1);

        await mgr.AddExchangeAsync("s1", "hâlâ bir dönüş yok", "kontrol ediyorum", NegativeSignal(), ct);
        session = await mgr.GetOrCreateAsync("s1", ct);
        session.State.ConsecutiveNegativeTurns.Should().Be(2,
            "iki olumsuz turdan sonra sayaç 2 olmalı — 4 değil");

        session.State.ConsecutiveNegativeTurns
            .Should().BeLessThan(WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative,
                "2 turda otomatik eskalasyon eşiği aşılmamalı");
    }

    [Fact]
    public async Task AddExchange_PositiveThenNegative_ResetsThenIncrements()
    {
        var mgr = BuildManager();
        var ct = TestContext.Current.CancellationToken;
        await mgr.GetOrCreateAsync("s1", ct);
        await mgr.MutateStateAsync("s1", s => s.ConsecutiveNegativeTurns = 5, ct);

        await mgr.AddExchangeAsync("s1", "her şey yolunda", "sevindim",
            new TurnSignals(null, WellKnown.Sentiments.Positive, 0.9), ct);
        (await mgr.GetOrCreateAsync("s1", ct)).State.ConsecutiveNegativeTurns.Should().Be(0);

        await mgr.AddExchangeAsync("s1", "yine olmadı", "üzgünüm", NegativeSignal(), ct);
        (await mgr.GetOrCreateAsync("s1", ct)).State.ConsecutiveNegativeTurns.Should().Be(1);
    }

    [Fact]
    public void CheckSentimentAlert_ThresholdReached_ShouldAlertTrue()
    {
        var svc = BuildService();
        var session = new AgentSession { SessionId = "s1" };
        session.State.ConsecutiveNegativeTurns = WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative;

        var result = svc.CheckSentimentAlert(session);

        result.ShouldAlert.Should().BeTrue();
        result.ConsecutiveNegativeTurns.Should().Be(WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative);
    }
}
