// Tests/Models/WellKnownTests.cs

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Tests;

public class WellKnownTests
{
    [Fact]
    public void AgentNames_Specialists_AllExpected()
    {
        WellKnown.AgentNames.Specialists.Should().Contain(new[]
        {
            WellKnown.AgentNames.Product,
            WellKnown.AgentNames.Order,
            WellKnown.AgentNames.Complaint,
        });
    }

    [Fact]
    public void AgentNames_All_ContainsAllRoles()
    {
        WellKnown.AgentNames.All.Should().Contain(WellKnown.AgentNames.Planning);
        WellKnown.AgentNames.All.Should().Contain(WellKnown.AgentNames.Response);
    }

    [Fact]
    public void HighRiskTools_Contains_OrderPlacement()
    {
        WellKnown.HighRiskTools.Should().Contain(WellKnown.ToolNames.OrderPlacement);
    }

    // ─── Yan etkili tool ↔ ajan eşlemesi tek kaynaktan türetilmeli ──────────────────
    // Bu bilgi eskiden beş ayrı yerde elle tekrarlanıyordu; Blazor admin panelindeki kopya
    // senkronunu kaybedince order_cancel_tool/return_request_tool onayları backend'de
    // 400 approval_reason_required ile kırılıyordu. Aşağıdaki testler türetmenin
    // bozulmadığını kilitler.

    [Fact]
    public void SideEffectToolOwners_CoversAllFourWriteTools()
    {
        WellKnown.SideEffectToolOwners.Keys.Should().BeEquivalentTo(new[]
        {
            WellKnown.ToolNames.OrderPlacement,
            WellKnown.ToolNames.OrderCancel,
            WellKnown.ToolNames.ReturnRequest,
            WellKnown.ToolNames.ComplaintRegistration
        });
    }

    [Fact]
    public void HighRiskTools_IsDerivedFrom_SideEffectToolOwners()
    {
        WellKnown.HighRiskTools.Should().BeEquivalentTo(WellKnown.SideEffectToolOwners.Keys);
    }

    [Fact]
    public void SideEffectToolsOf_Order_ReturnsThreeOrderTools()
    {
        WellKnown.SideEffectToolsOf(WellKnown.AgentNames.Order).Should().BeEquivalentTo(new[]
        {
            WellKnown.ToolNames.OrderPlacement,
            WellKnown.ToolNames.OrderCancel,
            WellKnown.ToolNames.ReturnRequest
        });
    }

    [Fact]
    public void SideEffectToolsOf_Complaint_ReturnsRegistrationOnly()
    {
        WellKnown.SideEffectToolsOf(WellKnown.AgentNames.Complaint)
            .Should().BeEquivalentTo(new[] { WellKnown.ToolNames.ComplaintRegistration });
    }

    [Fact]
    public void SideEffectToolsOf_ReadOnlyAgent_ReturnsEmpty()
    {
        WellKnown.SideEffectToolsOf(WellKnown.AgentNames.Product).Should().BeEmpty();
    }

    [Theory]
    [InlineData("order_placement_tool", true)]
    [InlineData("order_cancel_tool", true)]      // ← panelde eksikti, 400'e yol açıyordu
    [InlineData("return_request_tool", true)]    // ← panelde eksikti, 400'e yol açıyordu
    [InlineData("complaint_registration_tool", true)]
    [InlineData("order_status_tool", false)]
    [InlineData("product_inquiry_tool", false)]
    public void ApprovalRequest_ReasonRequired_MatchesBackendEnforcement(string toolName, bool expected)
    {
        // AdminEndpoints/AgentPanelEndpoints tam olarak WellKnown.HighRiskTools'a bakarak
        // 400 approval_reason_required dönüyor — panelin gördüğü bayrak da aynı kaynaktan
        // türemeli ki "isteğe bağlı" gösterip reddedilen onay durumu tekrar oluşmasın.
        new ApprovalRequest { ToolName = toolName }.ReasonRequired.Should().Be(expected);
    }
}
