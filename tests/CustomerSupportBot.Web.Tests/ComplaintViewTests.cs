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

    [Fact]
    public void Fields_FollowAvailableActions()
    {
        ComplaintView.ShowsResolutionField(["assign", "note"]).Should().BeFalse("beklemede müşteriye açıklama yazılmaz");
        ComplaintView.ShowsResolutionField(["resolve", "reject", "note"]).Should().BeTrue();
    }

    [Theory]
    [InlineData("resolve", "Yeni ürün gönderildi", "", "", true)]
    [InlineData("resolve", "", "iç not", "", false)]
    [InlineData("reject", " ", "", "", false)]
    [InlineData("note", "açıklama", "", "", false)]
    [InlineData("note", "", "kargoya soruldu", "", true)]
    [InlineData("reopen", "", "itiraz", "", true)]
    [InlineData("reopen", "x", "", "", false)]
    [InlineData("assign", "", "", "agent-jdoe", true)]
    [InlineData("assign", "", "", "", false)]
    [InlineData("claim", "", "", "", true)]
    public void CanSubmit(string action, string resolution, string note, string assignee, bool expected)
        => ComplaintView.CanSubmit(action, resolution, note, assignee).Should().Be(expected);

    [Theory]
    [InlineData("Beklemede", "tone-warn")]
    [InlineData("İncelemede", "tone-primary")]
    [InlineData("Çözüldü", "tone-success")]
    [InlineData("Reddedildi", "tone-danger")]
    [InlineData("?", "tone-neutral")]
    public void StatusTone(string status, string expected) => ComplaintView.StatusTone(status).Should().Be(expected);

    [Theory]
    [InlineData("warn", "SLA yaklaşıyor")]
    [InlineData("breach", "SLA aşıldı")]
    [InlineData("ok", null)]
    [InlineData("none", null)]
    public void SlaLabel(string sla, string? expected) => ComplaintView.SlaLabel(sla).Should().Be(expected);

    [Fact]
    public void Headline_FirstLine_Truncated()
    {
        ComplaintView.Headline("  Kutu ezik geldi.\nİkinci satır").Should().Be("Kutu ezik geldi.");
        ComplaintView.Headline(new string('a', 100), 20).Should().Be(new string('a', 19) + "…");
        ComplaintView.Headline("   ").Should().Be("Şikayet");
    }

    [Theory]
    [InlineData("created", "tone-neutral")]
    [InlineData("claimed", "tone-primary")]
    [InlineData("assigned", "tone-primary")]
    [InlineData("reopened", "tone-warn")]
    [InlineData("resolved", "tone-success")]
    [InlineData("rejected", "tone-danger")]
    [InlineData("note", "tone-neutral")]
    public void KindTone(string kind, string expected) => ComplaintView.KindTone(kind).Should().Be(expected);
}
