// Tests/Services/SessionStateServiceConcurrencyTests.cs
// Aynı session'a çakışan eşzamanlı isteklerde (çift-submit, çoklu sekme)
// ConsecutiveNegativeTurns++ sayaç kaybı yaşanmadığını doğrular.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

public class SessionStateServiceConcurrencyTests
{
    private static SessionStateService BuildService() =>
        new(Substitute.For<ISessionManager>(), NullLogger<SessionStateService>.Instance);

    private static ReasoningResult NegativeSentiment() => new()
    {
        Sentiment = WellKnown.Sentiments.Negative,
        SentimentScore = 0.1 // NegativeThreshold (0.35) altında
    };

    [Fact]
    public async Task UpdateSessionSentiment_ConcurrentCallsSameSession_NoLostIncrements()
    {
        var svc = BuildService();
        var session = new AgentSession { SessionId = "s1" };

        const int concurrentCalls = 50;
        var tasks = Enumerable.Range(0, concurrentCalls)
            .Select(_ => Task.Run(() => svc.UpdateSessionSentiment(session, NegativeSentiment())));

        await Task.WhenAll(tasks);

        // Kilit olmadan (oku-değiştir-yaz yarışı) bu sayı 50'den küçük çıkardı —
        // eşzamanlı iki çağrı aynı değeri okuyup aynı sonucu yazınca bir artış kaybolur.
        session.State.ConsecutiveNegativeTurns.Should().Be(concurrentCalls);
    }

    [Fact]
    public void UpdateSessionSentiment_PositiveThenNegative_ResetsThenIncrements()
    {
        var svc = BuildService();
        var session = new AgentSession { SessionId = "s1" };
        session.State.ConsecutiveNegativeTurns = 5;

        svc.UpdateSessionSentiment(session, new ReasoningResult
        {
            Sentiment = WellKnown.Sentiments.Positive,
            SentimentScore = 0.9
        });

        session.State.ConsecutiveNegativeTurns.Should().Be(0);

        svc.UpdateSessionSentiment(session, NegativeSentiment());
        session.State.ConsecutiveNegativeTurns.Should().Be(1);
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

    [Fact]
    public async Task UpdateSessionIntent_ConcurrentCallsSameSession_LastWriteWinsWithoutCrash()
    {
        var svc = BuildService();
        var session = new AgentSession { SessionId = "s1" };

        var tasks = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => svc.UpdateSessionIntentAsync(session, $"intent_{i}")));

        await Task.WhenAll(tasks);

        session.State.CurrentIntent.Should().StartWith("intent_");
    }
}
