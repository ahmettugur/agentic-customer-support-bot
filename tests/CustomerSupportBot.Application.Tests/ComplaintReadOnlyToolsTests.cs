// Tests/ComplaintReadOnlyToolsTests.cs
//
// Salt-okunur şikayet tool'ları. Asıl konu SAHİPLİK: bu tool'lar A2A ile dış sistemlere
// açıldığı için, başka bir müşterinin şikayetine erişimin hem engellenmesi hem de
// AYIRT EDİLEMEMESİ gerekir.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Tools;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class ComplaintReadOnlyToolsTests
{
    private const string Owner = "1027";
    private const string Other = "1001";

    private static ComplaintToolsService Build(IComplaintRepository complaints)
        => new(complaints, Substitute.For<IOrderRepository>());

    private static IComplaintRepository RepoWith(string complaintId, string customerId)
    {
        var repo = Substitute.For<IComplaintRepository>();
        repo.Get(complaintId).Returns(new ComplaintInfo
        {
            OrderId = "1030", CustomerId = customerId, Complaint = "Paket eksik geldi.", Status = "Beklemede"
        });
        return repo;
    }

    // ═══ Sahiplik — en kritik sınır ═══

    [Fact]
    public void ComplaintStatus_OwnComplaint_IsReturned()
    {
        var svc = Build(RepoWith("1005", Owner));

        var result = svc.ComplaintStatusTool("1005", Owner);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1005").And.Contain("Beklemede");
    }

    /// <summary>
    /// Başkasının şikayeti erişilebilir OLMAMALI. Bu düşerse A2A kanalı üzerinden herhangi bir
    /// partner, numara vererek başka müşterilerin şikayet içeriğini okuyabilir.
    /// </summary>
    [Fact]
    public void ComplaintStatus_OtherCustomersComplaint_IsDenied()
    {
        var svc = Build(RepoWith("1005", Other));

        var result = svc.ComplaintStatusTool("1005", Owner);

        result.Success.Should().BeFalse();
        result.Message.Should().NotContain("Paket eksik", "başkasının şikayet METNİ asla sızmamalı");
    }

    /// <summary>
    /// ENUMERATION KORUMASI: "var ama senin değil" ile "hiç yok" AYNI metni dönmeli. Ayrı
    /// metinler dönseydi dışarıdan numara taranarak hangi şikayetlerin var olduğu — ve dolaylı
    /// olarak başka müşterilerin şikayet hacmi — öğrenilebilirdi.
    /// </summary>
    [Fact]
    public void ComplaintStatus_ForeignAndMissing_ProduceIdenticalMessage()
    {
        var foreign = Build(RepoWith("1005", Other)).ComplaintStatusTool("1005", Owner);

        var emptyRepo = Substitute.For<IComplaintRepository>();
        emptyRepo.Get("1005").Returns((ComplaintInfo?)null);
        var missing = Build(emptyRepo).ComplaintStatusTool("1005", Owner);

        foreign.Message.Should().Be(missing.Message,
            "iki durum dışarıdan ayırt edilememeli");
    }

    /// <summary>
    /// Metinler aynı olsa da hata KODU farklı olmalı — trace/admin panelinde gerçek sebep
    /// görünmeli, aksi halde sızıntı denemeleri "bulunamadı" gürültüsünde kaybolur.
    /// </summary>
    [Fact]
    public void ComplaintStatus_ForeignAndMissing_HaveDifferentErrorCodes()
    {
        var foreign = Build(RepoWith("1005", Other)).ComplaintStatusTool("1005", Owner);

        var emptyRepo = Substitute.For<IComplaintRepository>();
        emptyRepo.Get("1005").Returns((ComplaintInfo?)null);
        var missing = Build(emptyRepo).ComplaintStatusTool("1005", Owner);

        foreign.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.CustomerIdMismatch);
        missing.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.ComplaintNotFound);
    }

    // ═══ Doğrulama ═══

    [Theory]
    [InlineData("", "1027")]
    [InlineData("1005", "")]
    [InlineData("   ", "1027")]
    public void ComplaintStatus_BlankInput_IsValidationError(string complaintId, string customerId)
    {
        var svc = Build(Substitute.For<IComplaintRepository>());

        svc.ComplaintStatusTool(complaintId, customerId).Success.Should().BeFalse();
    }

    // ═══ Listeleme ═══

    [Fact]
    public void GetAllComplaints_ReturnsOnlyRepositoryScopedResults()
    {
        var repo = Substitute.For<IComplaintRepository>();
        repo.GetByCustomer(Owner).Returns([
            ("1005", new ComplaintInfo { OrderId = "1030", CustomerId = Owner, Status = "Beklemede" }),
            ("1009", new ComplaintInfo { OrderId = "1057", CustomerId = Owner, Status = "Çözüldü" })
        ]);
        var svc = Build(repo);

        var result = svc.GetAllComplaintsTool(Owner);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("1005").And.Contain("1009");
        // Sorgu müşteriye göre yapılmalı — filtresiz çekip sonradan elemek sızıntı riskidir.
        repo.Received(1).GetByCustomer(Owner);
    }

    [Fact]
    public void GetAllComplaints_NoComplaints_IsNotFound_NotEmptySuccess()
    {
        var repo = Substitute.For<IComplaintRepository>();
        repo.GetByCustomer(Owner).Returns([]);

        var result = Build(repo).GetAllComplaintsTool(Owner);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(WellKnown.ToolErrorCodes.NoComplaintsForCustomer);
    }

    [Fact]
    public void GetAllComplaints_BlankCustomer_IsValidationError()
    {
        Build(Substitute.For<IComplaintRepository>()).GetAllComplaintsTool("").Success.Should().BeFalse();
    }
}
