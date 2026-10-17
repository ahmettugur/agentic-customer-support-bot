// MCP ile dışarı açılan tool kümesi. Yetki YAPISALDIR: yazma tool'ları kümede bulunmaz (bir if dalı değil),
// katalog tool'ları ile müşteri verisi tool'ları ayrı kümelerdedir (ayrı endpoint, ayrı token türü).

using CustomerSupportBot.Adapters.Agents.Mcp;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class McpToolCatalogTests
{
    private static McpToolCatalog Build()
    {
        var (tools, gate, _, _, _) = CheckpointTestRig.Dependencies();
        return new McpToolCatalog(gate, tools);
    }

    [Fact]
    public void CatalogSet_HasOnlyProductTools()
    {
        Build().Catalog.Select(t => t.Name).Should().BeEquivalentTo(
            [WellKnown.ToolNames.ProductInquiry, WellKnown.ToolNames.ProductList]);
    }

    [Fact]
    public void CustomerSet_HasOnlyReadOnlyOrderAndComplaintTools()
    {
        var names = Build().Customer.Select(t => t.Name).ToList();

        names.Should().NotBeEmpty();
        names.Should().NotContain(n => WellKnown.SideEffectToolOwners.ContainsKey(n),
            "dışarıdan gelen çağrı sipariş vermemeli/iptal etmemeli/iade ya da şikayet kaydı açmamalı");
        names.Should().NotContain([WellKnown.ToolNames.ProductInquiry, WellKnown.ToolNames.ProductList],
            "katalog ve müşteri verisi ayrı endpoint/token türüne bağlı");
    }

    [Fact]
    public void NoToolInEitherSet_IsSideEffecting()
    {
        var catalog = Build();

        catalog.Catalog.Concat(catalog.Customer).Select(t => t.Name)
            .Should().NotContain(n => WellKnown.SideEffectToolOwners.ContainsKey(n));
    }

    [Fact]
    public void ToolNames_AreUniqueWithinEachSet()
    {
        var catalog = Build();

        catalog.Catalog.Select(t => t.Name).Should().OnlyHaveUniqueItems();
        catalog.Customer.Select(t => t.Name).Should().OnlyHaveUniqueItems();
    }
}
