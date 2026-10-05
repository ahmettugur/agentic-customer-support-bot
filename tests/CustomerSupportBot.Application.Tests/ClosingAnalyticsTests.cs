// Analitik panelinde kapanış nedeni dağılımı ve en sık etiketler.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Conversations;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ClosingAnalyticsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AnalyticsPortService Build(InMemoryConversationDispositionStore store)
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var approvals = new InMemoryApprovalQueue(Options.Create(new ApprovalOptions()), Substitute.For<IApprovalExecutionRouter>(),
            NullLogger<InMemoryApprovalQueue>.Instance);
        return new AnalyticsPortService(new InMemoryRatingStore(NullLogger<InMemoryRatingStore>.Instance), sessions, approvals,
            new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance), NullLogger<AnalyticsPortService>.Instance,
            dispositions: store, closing: Options.Create(new ConversationClosingOptions()));
    }

    private static ConversationDisposition D(string reason, params string[] tags) =>
        new() { SessionId = $"s-{Guid.NewGuid():N}", ReasonCode = reason, Tags = [.. tags], ClosedAt = DateTime.UtcNow };

    [Fact]
    public async Task Dashboard_ShowsReasonDistribution_AndTopTags()
    {
        var store = new InMemoryConversationDispositionStore();
        foreach (var d in new[] { D("resolved", "kargo"), D("resolved", "kargo", "iade"), D("follow_up_required", "iade"), D("eski_neden") })
            await store.AddAsync(d, Ct);

        var dashboard = await Build(store).GetDashboardAsync(Ct);

        dashboard.ClosingReasons.Should().Equal(
            new ClosingReasonCount("resolved", "Çözüldü", 2),
            new ClosingReasonCount("follow_up_required", "Takip gerekiyor", 1),
            new ClosingReasonCount("eski_neden", "eski_neden", 1));   // listeden kaldırılmış neden kod adıyla
        dashboard.TopTags.Should().Equal(new TagUsage("iade", 2), new TagUsage("kargo", 2));
    }

    [Fact]
    public async Task Dashboard_WithoutDispositions_HasEmptyLists()
    {
        var dashboard = await Build(new InMemoryConversationDispositionStore()).GetDashboardAsync(Ct);

        dashboard.ClosingReasons.Should().BeEmpty();
        dashboard.TopTags.Should().BeEmpty();
    }
}
