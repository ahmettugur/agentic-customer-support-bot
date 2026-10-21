// Adapters.Agents/Mcp/McpToolCatalog.cs
// MCP (Model Context Protocol) ile DIŞ SİSTEMLERE açılan tool kümeleri.
//
// A2A kanalıyla aynı ilkeler: yetki yapısaldır (yazma tool'ları kümede YOKTUR; A2AAgentCatalog.ReadOnlyOnly
// çalışma zamanında doğrular), müşteri kimliği LLM'den değil ambient bağlamdan gelir, katalog sorguları ile
// müşteri verisi ayrı kümelerdedir (ayrı endpoint, ayrı token türü). MCP'de araya LLM girmez: tool doğrudan çalışır.

using CustomerSupportBot.Adapters.Agents.A2A;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Mcp;

public sealed class McpToolCatalog
{
    /// <summary>Ürün kataloğu: müşteri kimliği gerektirmez (Partner token'ı yeter).</summary>
    public IReadOnlyList<AIFunction> Catalog { get; }

    /// <summary>
    /// Salt-okunur sipariş ve şikayet sorguları. Müşteri verisi döndürür; çağrıdan ÖNCE ambient müşteri kimliği
    /// kurulmuş olmalıdır (tek müşteriye kilitli özne token'ı — bkz. Api/Endpoints/McpEndpoints).
    /// </summary>
    public IReadOnlyList<AIFunction> Customer { get; }

    public McpToolCatalog(ApprovalGateService approvalGate, ICustomerSupportToolsService tools)
    {
        Catalog = ReadOnly(
            AIFunctionFactory.Create(tools.ProductInquiryTool,
                new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductInquiry }),
            AIFunctionFactory.Create(tools.ProductListTool,
                new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductList }));

        // A2A ile aynı builder'lar YENİDEN KULLANILIR (kopyalanmaz): müşteri kimliğini ambient bağlamdan alma garantisi
        // de böylece aynen taşınır.
        Customer = ReadOnly(
            approvalGate.BuildOrderStatusTool(),
            approvalGate.BuildGetLastOrderTool(),
            approvalGate.BuildGetAllOrdersTool(),
            approvalGate.BuildComplaintStatusTool(),
            approvalGate.BuildGetAllComplaintsTool());
    }

    private static IReadOnlyList<AIFunction> ReadOnly(params AIFunction[] functions) =>
        A2AAgentCatalog.ReadOnlyOnly(functions).Cast<AIFunction>().ToList();
}
