// Sesli görüşmede modele tanıtılan tool seti.
//
// Sesli kanal yazılı sohbetin iş tool'larının TAMAMINI sunmalı. Liste elle tutulduğu için
// yazılı tarafa yeni bir iş tool'u eklenip sese eklenmezse bu test düşer.

using System.Text.Json;
using CustomerSupportBot.Adapters.AI.Realtime;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.AI.Tests;

public class RealtimeFunctionToolsTests
{
    private static readonly string[] BusinessTools =
    [
        WellKnown.ToolNames.ProductInquiry,
        WellKnown.ToolNames.ProductList,
        WellKnown.ToolNames.OrderStatus,
        WellKnown.ToolNames.GetLastOrder,
        WellKnown.ToolNames.GetAllOrders,
        WellKnown.ToolNames.ComplaintStatus,
        WellKnown.ToolNames.GetAllComplaints,
        WellKnown.ToolNames.OrderPlacement,
        WellKnown.ToolNames.OrderCancel,
        WellKnown.ToolNames.ReturnRequest,
        WellKnown.ToolNames.ComplaintRegistration,
        WellKnown.ToolNames.HumanHandoff,
    ];

    [Fact]
    public void Voice_ExposesEveryBusinessTool()
    {
        new RealtimeFunctionTools().GetToolNames().Should().Contain(BusinessTools);
    }

    [Fact]
    public void EveryAdvertisedName_HasASchema_AndViceVersa()
    {
        var tools = new RealtimeFunctionTools();
        var schemaNames = tools.GetToolDefinitions()
            .Select(d => JsonSerializer.SerializeToElement(d).GetProperty("name").GetString())
            .ToList();

        schemaNames.Should().BeEquivalentTo(tools.GetToolNames());
    }

    [Theory]
    [InlineData("order_placement_tool")]
    [InlineData("order_cancel_tool")]
    [InlineData("return_request_tool")]
    [InlineData("complaint_registration_tool")]
    public void SideEffectTools_TellTheModelTheyGoToApproval(string name)
    {
        var def = new RealtimeFunctionTools().GetToolDefinitions()
            .Select(d => JsonSerializer.SerializeToElement(d))
            .Single(d => d.GetProperty("name").GetString() == name);

        def.GetProperty("description").GetString().Should().Contain("onay",
            "model, işlemi tamamlandı diye sunmamalı — talep insan onayına gider");
    }
}
