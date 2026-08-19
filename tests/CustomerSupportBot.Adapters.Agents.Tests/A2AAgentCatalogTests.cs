// Tests/A2AAgentCatalogTests.cs
//
// A2A ile dış sistemlere açılan ajanların GÜVENLİK sınırları.
//
// Buradaki testlerin asıl konusu "ajan doğru cevap veriyor mu" değil — bu kanal dış sistemlere
// açık olduğu için asıl soru ŞU: ajana verilmemesi gereken bir yetenek sessizce sızmış mı?
// İki sızıntı yolu var ve ikisi de burada kilitleniyor:
//   1) Yan etkili bir tool'un A2A ajanına eklenmesi (sipariş oluşturma/iptal/iade, şikayet kaydı)
//   2) Workflow ajanlarının iç akıl yürütme şemasının (SpecialistReasoningSchema) dışarı taşması

using CustomerSupportBot.Adapters.Agents.A2A;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;
using CustomerSupportBot.Application.Services.A2A;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class A2AAgentCatalogTests
{
    // ═══ ReadOnlyOnly — yan etkili tool'a karşı çalışma zamanı bariyeri ═══

    /// <summary>
    /// Yan etkili tool listesi (WellKnown.SideEffectToolOwners) ileride büyüyecek ve o an bu
    /// dosyayı kimse açmayacak. Bariyer olmasaydı yeni bir yazma tool'u A2A ajanına yanlışlıkla
    /// eklendiğinde hata ancak DIŞ BİR SİSTEM onu çağırdığında fark edilirdi.
    /// </summary>
    [Theory]
    [InlineData(WellKnown.ToolNames.OrderPlacement)]
    [InlineData(WellKnown.ToolNames.OrderCancel)]
    [InlineData(WellKnown.ToolNames.ReturnRequest)]
    [InlineData(WellKnown.ToolNames.ComplaintRegistration)]
    public void ReadOnlyOnly_RejectsEverySideEffectingTool(string toolName)
    {
        var writeTool = AIFunctionFactory.Create(() => "yazma işlemi", name: toolName);

        var act = () => A2AAgentCatalog.ReadOnlyOnly(writeTool);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{toolName}*", "hangi tool'un reddedildiği hata mesajından anlaşılmalı");
    }

    /// <summary>
    /// Kapsam bekçisi: SideEffectToolOwners'a yeni bir tool eklenirse bu test onun da
    /// reddedildiğini otomatik doğrular — InlineData listesini güncellemeyi unutmak
    /// sessiz bir boşluk yaratmasın.
    /// </summary>
    [Fact]
    public void ReadOnlyOnly_RejectsEveryToolInSideEffectRegistry_NotJustTheKnownFour()
    {
        foreach (var toolName in WellKnown.SideEffectToolOwners.Keys)
        {
            var writeTool = AIFunctionFactory.Create(() => "x", name: toolName);
            var act = () => A2AAgentCatalog.ReadOnlyOnly(writeTool);
            act.Should().Throw<InvalidOperationException>($"'{toolName}' yan etkili kayıtta ama reddedilmedi");
        }
    }

    [Theory]
    [InlineData(WellKnown.ToolNames.OrderStatus)]
    [InlineData(WellKnown.ToolNames.GetLastOrder)]
    [InlineData(WellKnown.ToolNames.GetAllOrders)]
    [InlineData(WellKnown.ToolNames.ProductInquiry)]
    [InlineData(WellKnown.ToolNames.ProductList)]
    [InlineData(WellKnown.ToolNames.ComplaintStatus)]
    [InlineData(WellKnown.ToolNames.GetAllComplaints)]
    public void ReadOnlyOnly_AllowsReadOnlyTools(string toolName)
    {
        var readTool = AIFunctionFactory.Create(() => "okuma", name: toolName);

        var result = A2AAgentCatalog.ReadOnlyOnly(readTool);

        result.Should().ContainSingle();
    }

    /// <summary>Karışık listede tek bir yazma tool'u bile tümünü reddettirmeli.</summary>
    [Fact]
    public void ReadOnlyOnly_MixedList_WithOneWriteTool_Throws()
    {
        var ok = AIFunctionFactory.Create(() => "x", name: WellKnown.ToolNames.OrderStatus);
        var bad = AIFunctionFactory.Create(() => "x", name: WellKnown.ToolNames.OrderCancel);

        var act = () => A2AAgentCatalog.ReadOnlyOnly(ok, bad);

        act.Should().Throw<InvalidOperationException>();
    }

    // ═══ Gerçek katalog kurulumu — bariyerin FİİLEN çalıştığı yer ═══

    private static A2AAgentCatalog BuildCatalog()
    {
        var approvalOpts = Options.Create(new ApprovalOptions { Enabled = false });
        var sink = Substitute.For<IEscalationSink>();
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(Arg.Any<string>()).Returns("test talimatı");

        var approvalGate = new ApprovalGateService(
            Substitute.For<IApprovalQueue>(),
            approvalOpts,
            sink,
            Substitute.For<IApprovalContextAccessor>(),
            Substitute.For<ICustomerSupportToolsService>(),
            new EscalationPolicyService(sink, approvalOpts));

        return new A2AAgentCatalog(
            Substitute.For<IChatClient>(),
            prompts,
            approvalGate,
            Substitute.For<ICustomerSupportToolsService>(),
            Options.Create(new A2AOptions()));
    }

    /// <summary>
    /// ASIL REGRESYON TESTİ: kataloğu gerçekten kurar. Bariyer yalnızca kurulum sırasında
    /// çalıştığı için, yan etkili bir tool ajan listesine eklenirse (gerçekçi sızıntı senaryosu)
    /// hata BURADA yakalanır — izole ReadOnlyOnly testleri onu göremez, çünkü onlar bariyeri
    /// elle çağırır, üretimdeki tool listesine hiç bakmaz.
    /// </summary>
    [Fact]
    public void Constructor_BuildsBothAgents_WithoutTrippingReadOnlyBarrier()
    {
        var catalog = BuildCatalog();

        catalog.Product.Should().NotBeNull();
        catalog.Order.Should().NotBeNull();
        catalog.Complaint.Should().NotBeNull();
        catalog.Product.Name.Should().Be(A2AAgentNames.Product);
        catalog.Order.Name.Should().Be(A2AAgentNames.Order);
        catalog.Complaint.Name.Should().Be(A2AAgentNames.Complaint);
    }

    // ═══ Ajan adları — köprünün iki ucu aynı adı kullanmalı ═══

    /// <summary>
    /// Adlar hem AddA2AServer kaydında hem endpoint/AgentCard tarafında kullanılır. Serbest
    /// metin olarak iki yere yazılsalardı biri değiştiğinde köprü ancak çalışma zamanında kopardı.
    /// </summary>
    [Fact]
    public void AgentNames_AreDistinct_AndNotEmpty()
    {
        var names = new[] { A2AAgentNames.Product, A2AAgentNames.Order, A2AAgentNames.Complaint };

        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => !string.IsNullOrWhiteSpace(n));
    }

    /// <summary>
    /// A2A ajanları workflow ajanlarıyla AYNI ADI taşımamalı — ikisi ayrı örnekler, ayrı
    /// yetki seviyeleri. Aynı ad, log/trace okurken hangi kanalın çalıştığını belirsizleştirir.
    /// </summary>
    [Fact]
    public void AgentNames_DoNotCollideWithWorkflowAgents()
    {
        var a2aNames = new[] { A2AAgentNames.Product, A2AAgentNames.Order, A2AAgentNames.Complaint };

        a2aNames.Should().NotIntersectWith(WellKnown.AgentNames.All);
    }
}
