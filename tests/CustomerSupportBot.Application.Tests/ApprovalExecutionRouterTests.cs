// Onaylanmış bir order_placement_tool talebinin, admin kararından sonra doğru
// satırlarla yürütülmesini doğrular.
//
// Neden ayrı bir test dosyası: onay kaydı Postgres'e yazılıp geri okunduğunda
// Parameters sözlüğündeki değerler JsonElement'e dönüşür. Çok ürünlü sipariş bu
// yolda bir NESNE DİZİSİ taşır — sessizce boş listeye düşerse admin sipariş
// onaylar ama hiçbir ürün sipariş edilmez.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Domain.Model;
using NSubstitute;

namespace CustomerSupportBot.Application.Tests;

public class ApprovalExecutionRouterTests
{
    private static ApprovalRequest Request(Dictionary<string, object?> parameters) => new()
    {
        ToolName = WellKnown.ToolNames.OrderPlacement,
        CustomerId = "1027",
        Parameters = parameters
    };

    /// <summary>
    /// Onay kaydının Postgres'e yazılıp geri okunmasını taklit eder: sözlük JSON'a
    /// serileşir, geri okunduğunda değerler <see cref="JsonElement"/> olur.
    /// </summary>
    private static Dictionary<string, object?> RoundTrip(Dictionary<string, object?> parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    }

    private static (ApprovalExecutionRouter Router, ICustomerSupportToolsService Tools) Build()
    {
        var tools = Substitute.For<ICustomerSupportToolsService>();
        tools.OrderPlacementTool(Arg.Any<IReadOnlyList<OrderLineRequest>>(), Arg.Any<string>())
            .Returns(ToolResult.Ok("oluşturuldu"));
        return (new ApprovalExecutionRouter(tools), tools);
    }

    [Fact]
    public async Task OrderPlacement_LiveParameters_PassesAllLines()
    {
        var (router, tools) = Build();

        await router.ExecuteAsync(Request(new Dictionary<string, object?>
        {
            ["lines"] = new[] { new OrderLineRequest("Kahve", 2), new OrderLineRequest("Chai", 1) },
            ["customerId"] = "1027"
        }), TestContext.Current.CancellationToken);

        tools.Received(1).OrderPlacementTool(
            Arg.Is<IReadOnlyList<OrderLineRequest>>(l =>
                l.Count == 2 && l[0].ProductName == "Kahve" && l[0].Quantity == 2
                             && l[1].ProductName == "Chai" && l[1].Quantity == 1),
            "1027");
    }

    [Fact]
    public async Task OrderPlacement_AfterJsonRoundTrip_PassesAllLines()
    {
        var (router, tools) = Build();

        var parameters = RoundTrip(new Dictionary<string, object?>
        {
            ["lines"] = new[] { new OrderLineRequest("Kahve", 2), new OrderLineRequest("Chai", 1) },
            ["customerId"] = "1027"
        });

        await router.ExecuteAsync(Request(parameters), TestContext.Current.CancellationToken);

        tools.Received(1).OrderPlacementTool(
            Arg.Is<IReadOnlyList<OrderLineRequest>>(l =>
                l.Count == 2 && l[0].ProductName == "Kahve" && l[0].Quantity == 2
                             && l[1].ProductName == "Chai" && l[1].Quantity == 1),
            "1027");
    }

    [Fact]
    public async Task OrderPlacement_QuantityAsJsonString_IsParsed()
    {
        // Bazı LLM/serileştirme yolları sayıyı string olarak üretebilir.
        var (router, tools) = Build();

        var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            """{"lines":[{"productName":"Kahve","quantity":"3"}],"customerId":"1027"}""")!;

        await router.ExecuteAsync(Request(parameters), TestContext.Current.CancellationToken);

        tools.Received(1).OrderPlacementTool(
            Arg.Is<IReadOnlyList<OrderLineRequest>>(l => l.Count == 1 && l[0].Quantity == 3),
            "1027");
    }

    [Fact]
    public async Task OrderPlacement_MissingLines_PassesEmptyListSoToolRejects()
    {
        // Satır okunamadığında sessizce "bir şeyler" uydurulmamalı; boş liste tool
        // tarafında ValidationError'a düşer ve onay sonucu kullanıcıya öyle yansır.
        var (router, tools) = Build();

        await router.ExecuteAsync(
            Request(new Dictionary<string, object?> { ["customerId"] = "1027" }),
            TestContext.Current.CancellationToken);

        tools.Received(1).OrderPlacementTool(
            Arg.Is<IReadOnlyList<OrderLineRequest>>(l => l.Count == 0), "1027");
    }
}
