// Trace'e araç çağrısı kaydı: FunctionCall/FunctionResult eşleşir, başarı ToolResult'tan okunur,
// aynı çağrı (CallId) ikinci kez yazılmaz. Performans ekranının "hangi araç hata veriyor" verisi buradan gelir.

using System.Text.Json;
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class ToolCallRecordingTests
{
    private static ChatMessage Call(string id, string tool) =>
        new(ChatRole.Assistant, [new FunctionCallContent(id, tool, new Dictionary<string, object?> { ["orderId"] = "1030" })]);

    private static ChatMessage Result(string id, object? result) =>
        new(ChatRole.Tool, [new FunctionResultContent(id, result)]);

    [Fact]
    public void PairsCallsWithResults_NormalizesAgent_AndReadsOutcome()
    {
        var trace = new ReasoningTrace();
        List<ChatMessage> messages =
        [
            Call("c1", "order_status_tool"), Result("c1", ToolResult.Ok("Sipariş 1030 kargoda")),
            Call("c2", "order_cancel_tool"), Result("c2", ToolResult.NotFound("ORDER_NOT_FOUND", "Sipariş bulunamadı."))
        ];

        WorkflowTraceEventProcessor.RecordToolCalls(trace, messages, "OrderAgent_35e65735d631471088b174657c3f5dda");

        trace.ToolCalls.Should().HaveCount(2);
        trace.ToolCalls[0].Should().Match<ToolInvocation>(t =>
            t.ToolName == "order_status_tool" && t.AgentName == "OrderAgent" && t.Success && t.Signature == "c1" && t.ResultSummary == null);
        trace.ToolCalls[1].Success.Should().BeFalse();
        trace.ToolCalls[1].ResultSummary.Should().Be("ORDER_NOT_FOUND: Sipariş bulunamadı.");
    }

    [Fact]
    public void SameCallSeenAgain_IsNotDuplicated()
    {
        var trace = new ReasoningTrace();
        List<ChatMessage> messages = [Call("c1", "order_status_tool"), Result("c1", ToolResult.Ok("ok"))];

        WorkflowTraceEventProcessor.RecordToolCalls(trace, messages, "OrderAgent_a");
        WorkflowTraceEventProcessor.RecordToolCalls(trace, messages, "ResponseAgent_b"); // geçmişte aynı çağrı

        trace.ToolCalls.Should().ContainSingle().Which.AgentName.Should().Be("OrderAgent");
    }

    [Fact]
    public void CallWithoutResult_IsSkippedUntilItCompletes()
    {
        var trace = new ReasoningTrace();

        WorkflowTraceEventProcessor.RecordToolCalls(trace, [Call("c1", "product_inquiry_tool")], "ProductAgent_x");
        trace.ToolCalls.Should().BeEmpty();

        WorkflowTraceEventProcessor.RecordToolCalls(trace,
            [Call("c1", "product_inquiry_tool"), Result("c1", ToolResult.Ok("stokta"))], "ProductAgent_x");
        trace.ToolCalls.Should().ContainSingle().Which.Success.Should().BeTrue();
    }

    [Fact]
    public void JsonResult_And_Exception_AreUnderstood()
    {
        var trace = new ReasoningTrace();
        var failedJson = JsonDocument.Parse("""{"success":false,"message":"Stok yetersiz","error":{"code":"OUT_OF_STOCK"}}""").RootElement;
        var crashed = new FunctionResultContent("c3", null) { Exception = new InvalidOperationException("bağlantı koptu") };

        WorkflowTraceEventProcessor.RecordToolCalls(trace,
        [
            Call("c1", "order_placement_tool"), Result("c1", failedJson),
            Call("c2", "product_list_tool"), Result("c2", "düz metin sonuç"),
            Call("c3", "complaint_status_tool"), new ChatMessage(ChatRole.Tool, [crashed])
        ], "OrderAgent");

        trace.ToolCalls.Select(t => t.Success).Should().Equal(false, true, false);
        trace.ToolCalls[0].ResultSummary.Should().Be("OUT_OF_STOCK: Stok yetersiz");
        trace.ToolCalls[2].ResultSummary.Should().Be("bağlantı koptu");
    }

    [Fact]
    public void LongFailureMessage_IsTruncated()
    {
        var trace = new ReasoningTrace();

        WorkflowTraceEventProcessor.RecordToolCalls(trace,
            [Call("c1", "order_status_tool"), Result("c1", ToolResult.SystemError("X", new string('a', 500)))], "OrderAgent");

        trace.ToolCalls[0].ResultSummary!.Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public void MessageAuthor_WinsOverExecutorFallback()
    {
        // Gerçek akışta çağrılar yalnızca final WorkflowOutputEvent'in tüm mesaj listesinde görünür;
        // ajan, mesajın yazarıdır (AuthorName = "ProductAgent").
        var trace = new ReasoningTrace();
        var call = Call("c1", "product_inquiry_tool");
        call.AuthorName = "ProductAgent";
        var result = Result("c1", ToolResult.Ok("stokta"));
        result.AuthorName = "ProductAgent";

        WorkflowTraceEventProcessor.RecordToolCalls(trace, [call, result], fallbackExecutorId: null);

        trace.ToolCalls.Should().ContainSingle().Which.AgentName.Should().Be("ProductAgent");
    }
}
