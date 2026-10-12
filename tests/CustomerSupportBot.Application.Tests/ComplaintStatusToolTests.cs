// Bot şikayet durumunu söylerken: kapanmışsa açıklama + tarih; iç not ve temsilci asla.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class ComplaintStatusToolTests
{
    private static ComplaintToolsService Build(ComplaintInfo complaint, out IComplaintRepository repo)
    {
        repo = Substitute.For<IComplaintRepository>();
        repo.Get("1006").Returns(complaint);
        var orders = Substitute.For<IOrderRepository>();
        orders.Get("1030").Returns(new OrderInfo { CustomerId = "1001", Status = "Teslim Edildi", Lines = [new OrderLine("Kahve", 1)] });
        return new ComplaintToolsService(repo, orders);
    }

    [Fact]
    public void StatusTool_ShowsResolution_NotInternalNote()
    {
        var svc = Build(new ComplaintInfo
        {
            OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = WellKnown.ComplaintStatuses.Resolved,
            Resolution = "Yeni ürün gönderildi.", AssignedAgentId = "a1", UpdatedAt = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc)
        }, out _);

        var r = svc.ComplaintStatusTool("1006", "1001");

        r.Success.Should().BeTrue();
        r.Message.Should().Contain("Durum: Çözüldü").And.Contain("Açıklama: Yeni ürün gönderildi.")
            .And.Contain("Son güncelleme: 12.10.2026").And.NotContain("a1");
    }

    [Fact]
    public void StatusTool_Open_HasNoResolutionLine()
    {
        var svc = Build(new ComplaintInfo
        {
            OrderId = "1030", CustomerId = "1001", Complaint = "kırık", Status = WellKnown.ComplaintStatuses.InProgress,
            Resolution = "eski açıklama"
        }, out _);

        svc.ComplaintStatusTool("1006", "1001").Message.Should().Contain("Durum: İncelemede").And.NotContain("Açıklama");
    }

    [Fact]
    public void Registration_StoresApprovalId()
    {
        var svc = Build(new ComplaintInfo(), out var repo);
        repo.Create(Arg.Any<ComplaintInfo>()).Returns("1007");

        svc.ComplaintRegistrationTool("1030", "Ürün kırık geldi, değişim istiyorum", "1001", approvalId: "apr-9").Success.Should().BeTrue();

        repo.Received(1).Create(Arg.Is<ComplaintInfo>(c => c.ApprovalId == "apr-9"));
    }
}
