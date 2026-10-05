// Sunucu ↔ panel sözleşmesi: /analytics/dashboard sunucuda Domain.AnalyticsDashboard olarak yazılır,
// panelde Web.Models.AnalyticsDashboard olarak okunur. Alan adları ayrışınca değer sessizce 0/null olur —
// panelde "Henüz onay işlemi yok", "0 mesaj/oturum" görünüyordu. Bu testler iki ucu birlikte kilitler.

using System.Text.Json;
using ServerDashboard = CustomerSupportBot.Domain.Model.AnalyticsDashboard;
using WebDashboard = CustomerSupportBot.Web.Models.AnalyticsDashboard;

namespace CustomerSupportBot.Web.Tests;

public class AnalyticsDashboardContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static WebDashboard RoundTrip(ServerDashboard server) =>
        JsonSerializer.Deserialize<WebDashboard>(JsonSerializer.Serialize(server, Web), Web)!;

    [Fact]
    public void ServerValues_ReachThePanel()
    {
        var web = RoundTrip(new ServerDashboard
        {
            TotalSessions = 10, TotalMessages = 50, AverageSessionMessages = 5.0,
            AverageRating = 4.2, TotalRatings = 3, RatingDistribution = new() { [5] = 2, [4] = 1 },
            TotalApprovals = 7, ApprovedCount = 4, RejectedCount = 2, ExpiredCount = 1,
            TotalEscalations = 3, ResolvedEscalations = 2, DismissedEscalations = 1,
            AverageSentimentScore = 0.6, NegativeSessionCount = 2, SentimentAlertCount = 1,
            EligibleSessions = 8, ContainedSessions = 6, HumanInvolvedSessions = 2, ContainmentRate = 0.75,
            TotalLlmCostUsd = 1.5m, UnattributedLlmCostUsd = 0.3m, SessionsWithCost = 4,
            AverageCostPerConversationUsd = 0.3m, MedianCostPerConversationUsd = 0.25m
        });

        web.AverageSessionMessages.Should().Be(5.0);
        web.NegativeSessionCount.Should().Be(2);
        web.SentimentAlertCount.Should().Be(1);
        web.RatingDistribution!["5"].Should().Be(2);
        web.ApprovalStats.Should().Be(new CustomerSupportBot.Web.Models.ApprovalStats(7, 4, 2, 1));
        web.EscalationStats.Should().Be(new CustomerSupportBot.Web.Models.EscalationStats(3, 2, 1));
        web.ContainmentRate.Should().Be(0.75);
        web.ContainedSessions.Should().Be(6);
        web.HumanInvolvedSessions.Should().Be(2);
        web.AverageCostPerConversationUsd.Should().Be(0.3m);
        web.MedianCostPerConversationUsd.Should().Be(0.25m);
        web.TotalLlmCostUsd.Should().Be(1.5m);
    }

    [Fact]
    public void ClosingReasonsAndTags_ReachThePanel()
    {
        var web = RoundTrip(new ServerDashboard
        {
            ClosingReasons = [new("resolved", "Çözüldü", 3)],
            TopTags = [new("kargo", 2)]
        });

        web.ClosingReasons.Should().ContainSingle().Which.Should().Be(new CustomerSupportBot.Web.Models.ClosingReasonCountItem("resolved", "Çözüldü", 3));
        web.TopTags.Should().ContainSingle().Which.Should().Be(new CustomerSupportBot.Web.Models.TagUsageItem("kargo", 2));
    }

    [Fact]
    public void EveryServerField_HasAPanelCounterpart()
    {
        var webNames = typeof(WebDashboard).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = typeof(ServerDashboard).GetProperties().Select(p => p.Name).Where(n => !webNames.Contains(n)).ToList();

        missing.Should().BeEmpty("sunucunun gönderdiği her alan panelde okunmalı; yoksa değer sessizce kaybolur");
    }
}
