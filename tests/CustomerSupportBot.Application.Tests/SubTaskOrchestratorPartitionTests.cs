// Agents/SubTaskOrchestratorPartitionTests.cs
// Paralel sub-task gruplama testleri (#E).

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Reasoning;

namespace CustomerSupportBot.Application.Tests;

public class SubTaskOrchestratorPartitionTests
{
    private static readonly ParallelExecutionOptions DefaultOpts = new()
    {
        Enabled = true,
        MaxDegreeOfParallelism = 4
    };

    private static SubTask Sub(int order, string agent, string desc = "x", string intent = "",
                               int[]? dependsOn = null) =>
        new()
        {
            Order = order,
            TargetAgent = agent,
            Description = desc,
            Intent = intent,
            Dependencies = (dependsOn ?? []).ToList()
        };

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

    [Theory]
    [InlineData(WellKnown.Intents.OrderInquiry)]
    [InlineData(WellKnown.Intents.OrderListing)]
    public void IsReadOnly_OrderAgentWithReadOnlyIntent_True(string intent)
    {
        DefaultOpts.IsReadOnly(Sub(1, WellKnown.AgentNames.Order, intent: intent)).Should().BeTrue();
    }

    [Theory]
    [InlineData(WellKnown.Intents.OrderCreation)]
    [InlineData(WellKnown.Intents.OrderCancellation)]
    [InlineData(WellKnown.Intents.ReturnRequest)]
    public void IsReadOnly_OrderAgentWithWriteIntent_False(string intent)
    {
        DefaultOpts.IsReadOnly(Sub(1, WellKnown.AgentNames.Order, intent: intent)).Should().BeFalse();
    }

    [Fact]
    public void Partition_TwoOrderStatusQueries_SingleParallelGroup()
    {
        // Gerçek kullanıcı senaryosu: "1042 ve 1043 numaralı siparişlerin durumu ne?"
        // İkisi de OrderAgent + sipariş_sorgulama — artık paralel çalışmalı.
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Order, intent: WellKnown.Intents.OrderInquiry),
            Sub(2, WellKnown.AgentNames.Order, intent: WellKnown.Intents.OrderInquiry)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(1);
        groups[0].Parallel.Should().BeTrue();
        groups[0].Items.Should().HaveCount(2);
    }

    [Fact]
    public void Partition_OrderInquiryThenOrderCancellation_TwoGroups()
    {
        // Sorgu (read) sonra iptal (write) — yan-etkili adımdan önce/sonra sıralama
        // korunmalı, ikisi tek paralel gruba alınmamalı.
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Order, intent: WellKnown.Intents.OrderInquiry),
            Sub(2, WellKnown.AgentNames.Order, intent: WellKnown.Intents.OrderCancellation)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(2);
        groups[0].Parallel.Should().BeTrue();
        groups[1].Parallel.Should().BeFalse();
    }

    // ─── Dependencies ────────────────────────────────────────────────────────────
    // DecomposedRunner paralel bir grubun elemanlarını AYNI history snapshot'ıyla
    // eşzamanlı başlatır — aynı batch'teki kardeşin sonucu diğerine görünmez.
    // Bu yüzden bildirilen bir öncül asla aynı batch'e alınmamalı. (Bu alan uzun süre
    // parse ediliyor ama hiç okunmuyordu.)

    [Fact]
    public void Partition_DependentReadOnlySubTasks_AreSplitIntoSeparateGroups()
    {
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Product),
            Sub(2, WellKnown.AgentNames.Product, dependsOn: [1])
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(2, "2 numaralı görev 1'in sonucunu bekliyor, aynı batch'e alınamaz");
        groups[0].Items.Single().Order.Should().Be(1);
        groups[1].Items.Single().Order.Should().Be(2);
        groups.Should().OnlyContain(g => g.Parallel, "ikisi de yan-etkisiz — sadece batch sınırı değişti");
    }

    [Fact]
    public void Partition_DependencyOnEarlierGroup_DoesNotSplitAgain()
    {
        // 1 ve 2 paralel, 3 yalnızca 1'e bağımlı. 1 ilk batch'te bitmiş olacağı için
        // (gruplar birbirini WhenAll ile bekler) 3'ün ayrı bir gruba düşmesi yeterli;
        // 3 ile 4 aynı batch'te kalabilmeli.
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Product),
            Sub(2, WellKnown.AgentNames.Product, dependsOn: [1]),
            Sub(3, WellKnown.AgentNames.Product, dependsOn: [1]),
            Sub(4, WellKnown.AgentNames.Product)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(2);
        groups[0].Items.Select(i => i.Order).Should().Equal(1);
        groups[1].Items.Select(i => i.Order).Should().Equal(2, 3, 4);
    }

    [Fact]
    public void Partition_IndependentSubTasks_WithEmptyDependencies_StayInOneGroup()
    {
        // Bağımlılık kontrolü, bağımsız görevleri gereksiz yere ayırmamalı.
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Product),
            Sub(2, WellKnown.AgentNames.Product),
            Sub(3, WellKnown.AgentNames.Product)
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(1);
        groups[0].Items.Should().HaveCount(3);
    }

    [Fact]
    public void Partition_DependenciesAmongSerialSubTasks_DoNotCreateExtraGroups()
    {
        // Seri grup zaten Order sırasıyla tek tek yürütülüyor — bağımlılık kendiliğinden
        // karşılanır, fazladan bölme yapmaya gerek yok.
        var subs = new[]
        {
            Sub(1, WellKnown.AgentNames.Order, intent: WellKnown.Intents.OrderCancellation),
            Sub(2, WellKnown.AgentNames.Complaint, dependsOn: [1])
        };

        var groups = SubTaskOrchestrator.Partition(subs, DefaultOpts);

        groups.Should().HaveCount(1);
        groups[0].Parallel.Should().BeFalse();
        groups[0].Items.Should().HaveCount(2);
    }
}
