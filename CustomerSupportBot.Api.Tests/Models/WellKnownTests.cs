// Tests/Models/WellKnownTests.cs

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Tests.Models;

public class WellKnownTests
{
    [Fact]
    public void AgentNames_Specialists_AllExpected()
    {
        WellKnown.AgentNames.Specialists.Should().Contain(new[]
        {
            WellKnown.AgentNames.ProductInquiry,
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
}
