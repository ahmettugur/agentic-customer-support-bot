// Şikayet paneli görüntü yardımcıları: SLA rengi, etiketler, yaş, rozet sayısı.

using CustomerSupportBot.Web.Helpers;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Tests;

public class ComplaintViewTests
{
    [Theory]
    [InlineData("breach", "sla-breach")]
    [InlineData("warn", "sla-warn")]
    [InlineData("ok", "sla-ok")]
    [InlineData("none", "")]
    [InlineData("??", "")]
    public void ToneClass(string sla, string expected) => ComplaintView.ToneClass(sla).Should().Be(expected);

    [Theory]
    [InlineData("claim", "Üstlen")]
    [InlineData("assign", "Ata")]
    [InlineData("resolve", "Çözüldü")]
    [InlineData("reject", "Reddet")]
    [InlineData("reopen", "Yeniden aç")]
    [InlineData("note", "Not ekle")]
    public void ActionLabel(string action, string expected) => ComplaintView.ActionLabel(action).Should().Be(expected);

    [Theory]
    [InlineData("created", "Oluşturuldu")]
    [InlineData("claimed", "Üstlenildi")]
    [InlineData("assigned", "Atandı")]
    [InlineData("resolved", "Çözüldü")]
    [InlineData("rejected", "Reddedildi")]
    [InlineData("reopened", "Yeniden açıldı")]
    [InlineData("note", "Not")]
    public void KindLabel(string kind, string expected) => ComplaintView.KindLabel(kind).Should().Be(expected);

    [Fact]
    public void Age_ReadsInMinutesHoursDays()
    {
        var now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
        ComplaintView.Age(now.AddMinutes(-5), now).Should().Be("5 dk");
        ComplaintView.Age(now.AddHours(-3), now).Should().Be("3 sa");
        ComplaintView.Age(now.AddDays(-2), now).Should().Be("2 gün");
    }

    [Fact]
    public void BadgeCount_AdminPending_AgentPendingPlusMine()
    {
        var c = new ComplaintCountsResponse(Pending: 4, InProgress: 7, AssignedToMe: 2);
        ComplaintView.BadgeCount(c, isAgent: false).Should().Be(4);
        ComplaintView.BadgeCount(c, isAgent: true).Should().Be(6);
    }
}
