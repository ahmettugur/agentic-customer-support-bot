using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class AgentChipLogicTests
{
    [Fact]
    public void ExecutorIdAndPlainHintTag_AreTheSameAgent()
        => AgentChipLogic.SameAgent("ProductAgent_ProductAgent", "ProductAgent").Should().BeTrue();

    [Fact]
    public void DifferentAgents_AreNotTheSame()
        => AgentChipLogic.SameAgent("ProductAgent_ProductAgent", "OrderAgent").Should().BeFalse();

    [Fact]
    public void SubTaskNames_KeepTheirNumber()
        => AgentChipLogic.SameAgent("SubTask#1", "SubTask#2").Should().BeFalse();

    [Fact]
    public void EmptyNames_AreNeverTheSame()
        => AgentChipLogic.SameAgent("", "").Should().BeFalse();
}
