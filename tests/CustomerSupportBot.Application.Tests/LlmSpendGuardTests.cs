// LLM harcama limiti: kapsamlar, sınırsız (0), dönem geçişi, tohumlama, fail-open, tek seferlik uyarı.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services.Budget;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class LlmSpendGuardTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Harness(
        LlmSpendGuard Guard, InMemoryLlmSpendCounter Counter, FakeTimeProvider Clock, ILlmCallPersistencePort Persistence,
        InMemoryNotificationLedger Ledger, IEmailSender Email, List<EmailMessage> Sent);

    private static Harness Build(Action<LlmBudgetOptions>? configure = null)
    {
        var options = new LlmBudgetOptions { Enabled = true };
        configure?.Invoke(options);
        var monitor = Substitute.For<IOptionsMonitor<LlmBudgetOptions>>();
        monitor.CurrentValue.Returns(options);
        var counter = new InMemoryLlmSpendCounter();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 23, 0, 0, TimeSpan.Zero));
        var persistence = Substitute.For<ILlmCallPersistencePort>();
        persistence.GetTotalCostSinceAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(0m);
        var ledger = new InMemoryNotificationLedger();
        var sent = new List<EmailMessage>();
        var email = Substitute.For<IEmailSender>();
        email.IsEnabled.Returns(true);
        email.SendAsync(Arg.Do<EmailMessage>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var guard = new LlmSpendGuard(monitor, counter, NullLogger<LlmSpendGuard>.Instance, clock, persistence, ledger, email);
        return new Harness(guard, counter, clock, persistence, ledger, email, sent);
    }

    [Fact]
    public async Task Disabled_NeverBlocks_AndDoesNotCount()
    {
        var h = Build(o => { o.Enabled = false; o.DailyLimitUsd = 1; });

        await h.Guard.RecordAsync(5m, "s1", Ct);

        (await h.Guard.CheckAsync("s1", Ct)).Should().BeNull();
        h.Counter.Ttls.Should().BeEmpty();
    }

    [Fact]
    public async Task DailyLimit_BlocksOnceReached()
    {
        var h = Build(o => o.DailyLimitUsd = 5);
        await h.Guard.RecordAsync(4m, null, Ct);
        (await h.Guard.CheckAsync(null, Ct)).Should().BeNull();

        await h.Guard.RecordAsync(1m, null, Ct);

        (await h.Guard.CheckAsync(null, Ct)).Should().Be(new LlmBudgetExceeded(LlmBudgetScope.Daily, 5m, 5m));
    }

    [Fact]
    public async Task MonthlyLimit_SurvivesTheDayRollover_DailyDoesNot()
    {
        var h = Build(o => { o.DailyLimitUsd = 5; o.MonthlyLimitUsd = 8; });
        await h.Guard.RecordAsync(5m, null, Ct);
        (await h.Guard.CheckAsync(null, Ct))!.Scope.Should().Be(LlmBudgetScope.Daily);

        h.Clock.Advance(TimeSpan.FromHours(2));   // ertesi UTC günü, aynı ay
        (await h.Guard.CheckAsync(null, Ct)).Should().BeNull("yeni gün");
        await h.Guard.RecordAsync(3m, null, Ct);

        (await h.Guard.CheckAsync(null, Ct)).Should().Be(new LlmBudgetExceeded(LlmBudgetScope.Monthly, 8m, 8m));
    }

    [Fact]
    public async Task ConversationLimit_AppliesOnlyToThatConversation()
    {
        var h = Build(o => o.PerConversationLimitUsd = 1);
        await h.Guard.RecordAsync(1.2m, "s1", Ct);

        (await h.Guard.CheckAsync("s1", Ct)).Should().Be(new LlmBudgetExceeded(LlmBudgetScope.Conversation, 1m, 1.2m));
        (await h.Guard.CheckAsync("s2", Ct)).Should().BeNull();
        (await h.Guard.CheckAsync(null, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task ZeroMeansUnlimited()
    {
        var h = Build();
        await h.Guard.RecordAsync(10_000m, "s1", Ct);

        (await h.Guard.CheckAsync("s1", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task MissingCounter_IsSeededFromTheDatabase()
    {
        var h = Build(o => { o.DailyLimitUsd = 5; o.MonthlyLimitUsd = 100; });
        var dayStart = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        var monthStart = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        h.Persistence.GetTotalCostSinceAsync(dayStart, Arg.Any<CancellationToken>()).Returns(7m);
        h.Persistence.GetTotalCostSinceAsync(monthStart, Arg.Any<CancellationToken>()).Returns(20m);

        (await h.Guard.CheckAsync(null, Ct)).Should().Be(new LlmBudgetExceeded(LlmBudgetScope.Daily, 5m, 7m));
        await h.Guard.CheckAsync(null, Ct);

        await h.Persistence.Received(1).GetTotalCostSinceAsync(dayStart, Arg.Any<CancellationToken>());
        (await h.Guard.GetStatusAsync(Ct)).MonthlySpentUsd.Should().Be(20m);
    }

    [Fact]
    public async Task CounterFailure_FailsOpen()
    {
        var h = Build(o => o.DailyLimitUsd = 1);
        h.Counter.Failure = new InvalidOperationException("redis kapalı");

        (await h.Guard.CheckAsync("s1", Ct)).Should().BeNull();
        await h.Guard.Invoking(g => g.RecordAsync(5m, "s1", Ct)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task CrossingTheWarningAndTheLimit_AlertsOnceEach()
    {
        var h = Build(o => { o.DailyLimitUsd = 10; o.WarningThresholdPercent = 80; o.AlertEmails = ["ops@example.com", "fin@example.com"]; });

        await h.Guard.RecordAsync(7m, null, Ct);
        h.Sent.Should().BeEmpty();
        await h.Guard.RecordAsync(2m, null, Ct);    // 9 ≥ 8
        await h.Guard.RecordAsync(0.5m, null, Ct);  // eşik zaten geçildi
        await h.Guard.RecordAsync(1m, null, Ct);    // 10.5 ≥ 10
        await h.Guard.RecordAsync(1m, null, Ct);

        h.Sent.Select(m => (m.To, m.Subject)).Should().Equal(
            ("ops@example.com", "LLM bütçesi uyarısı: günlük harcama %80 eşiğini geçti"),
            ("fin@example.com", "LLM bütçesi uyarısı: günlük harcama %80 eşiğini geçti"),
            ("ops@example.com", "LLM bütçesi: günlük limit doldu"),
            ("fin@example.com", "LLM bütçesi: günlük limit doldu"));
        h.Sent[2].TextBody.Should().Contain("10.50").And.Contain("10.00");
    }

    [Fact]
    public async Task AlertEmailFailure_ReleasesTheClaim_SoItCanBeRetried()
    {
        var h = Build(o => { o.DailyLimitUsd = 10; o.AlertEmails = ["ops@example.com"]; });
        h.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("smtp")));

        await h.Guard.RecordAsync(9m, null, Ct);

        (await h.Ledger.TryClaimAsync("llm-budget:day:20261005:warning", Ct)).Should().BeTrue("başarısız gönderimde talep bırakılır");
    }

    [Fact]
    public async Task Status_ReportsLimitsAndSpend()
    {
        var h = Build(o => { o.DailyLimitUsd = 50; o.MonthlyLimitUsd = 1000; o.PerConversationLimitUsd = 1; });
        await h.Guard.RecordAsync(2.5m, "s1", Ct);

        (await h.Guard.GetStatusAsync(Ct)).Should().Be(new LlmBudgetStatus(true, 50m, 2.5m, 1000m, 2.5m, 1m, 80));
    }
}
