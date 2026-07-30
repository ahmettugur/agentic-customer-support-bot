// Agents/SubTaskOrchestratorPartitionTests.cs
// Paralel sub-task gruplama testleri (#E).

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Api.Tests.Agents;

public class SubTaskOrchestratorPartitionTests
{
    private static readonly ParallelExecutionOptions DefaultOpts = new()
    {
        Enabled = true,
        MaxDegreeOfParallelism = 4
    };

    private static SubTask Sub(int order, string agent, string desc = "x") =>
        new() { Order = order, TargetAgent = agent, Description = desc };

    [Fact]
    public void Partition_Empty_ReturnsEmpty() =>
        SubTaskOrchestrator.Partition(Array.Empty<SubTask>(), DefaultOpts).Should().BeEmpty();

    [Fact]
    public void Partition_AllReadOnly_SingleParallelGroup()
    {
        // Tek read-only agent: Product
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Product),
            Sub(2, WellKnown.AgentNames.Product)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(1);
        groups[0].Parallel.Should().BeTrue();
        groups[0].Items.Should().HaveCount(2);
    }

    [Fact]
    public void Partition_AllWriteAgents_SingleSerialGroup()
    {
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Order),
            Sub(2, WellKnown.AgentNames.Complaint)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(1);
        groups[0].Parallel.Should().BeFalse();
    }

    [Fact]
    public void Partition_MixedSequence_PreservesOrderedGroups()
    {
        // read, write, read — 3 grup: [P] (parallel-1), [O] (serial), [P] (parallel-1)
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Product),
            Sub(2, WellKnown.AgentNames.Order),
            Sub(3, WellKnown.AgentNames.Product)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(3);
        groups[0].Parallel.Should().BeTrue();
        groups[0].Items.Single().Order.Should().Be(1);
        groups[1].Parallel.Should().BeFalse();
        groups[1].Items.Single().TargetAgent.Should().Be(WellKnown.AgentNames.Order);
        groups[2].Parallel.Should().BeTrue();
        groups[2].Items.Single().Order.Should().Be(3);
    }

    [Fact]
    public void Partition_DisabledOptions_AllSerial()
    {
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Product),
            Sub(2, WellKnown.AgentNames.Product)
        };

        var opts = new ParallelExecutionOptions { Enabled = false };

        var groups = SubTaskOrchestrator.Partition(subs, opts);

        groups.Should().HaveCount(1);
        groups[0].Parallel.Should().BeFalse();
    }

    [Fact]
    public void Partition_RespectsOrderField_NotInputOrder()
    {
        var subs = new[]
        {
            Sub(3, WellKnown.AgentNames.Product),
            Sub(1, WellKnown.AgentNames.Product),
            Sub(2, WellKnown.AgentNames.Complaint)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        // sıralı: 1=P, 2=W, 3=P  3 grup
        groups.Should().HaveCount(3);
        groups[0].Items.Single().Order.Should().Be(1);
        groups[1].Items.Single().Order.Should().Be(2);
        groups[2].Items.Single().Order.Should().Be(3);
    }

    [Fact]
    public void IsReadOnly_UnknownAgent_False()
    {
        DefaultOpts.IsReadOnly(Sub(1, "UnknownAgent")).Should().BeFalse();
    }

    [Fact]
    public void IsReadOnly_EmptyAgent_False()
    {
        DefaultOpts.IsReadOnly(Sub(1, "")).Should().BeFalse();
    }

    [Fact]
    public void IsReadOnly_CaseInsensitive_True()
    {
        DefaultOpts.IsReadOnly(Sub(1, "productagent")).Should().BeTrue();
    }

    [Fact]
    public void IsReadOnly_OrderAgent_False()
    {
        DefaultOpts.IsReadOnly(Sub(1, WellKnown.AgentNames.Order)).Should().BeFalse();
    }
}
