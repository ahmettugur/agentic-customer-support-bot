using System.Text.Json;
using CustomerSupportBot.Api.Agents;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Tests.Agents;

public class ApprovalGateServiceToolBuilderTests
{
    private static ApprovalGateService Build(ApprovalOptions opts, IApprovalQueue? queue = null)
    {
        queue ??= new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        return new ApprovalGateService(
            queue,
            Options.Create(opts),
            sink,
            new ApprovalContextAccessor());
    }

    /// <summary>
    /// AIFunctionFactory wraps the result and may serialize it to JsonElement.
    /// Parse out the success/message fields uniformly.
    /// </summary>
    private static (bool success, string message) ParseResult(object? raw)
    {
        if (raw is ToolResult tr) return (tr.Success, tr.Message ?? "");
        if (raw is JsonElement je)
        {
            var success = je.TryGetProperty("success", out var s) && s.GetBoolean();
            var message = je.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            return (success, message);
        }
        return (false, raw?.ToString() ?? "");
    }

    [Fact]
    public async Task BuildOrderPlacementTool_ApprovalDisabled_PassesThroughToTool()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var svc = Build(opts);
        var fn = svc.BuildOrderPlacementTool();

        var product = FakeDatabase.ProductCatalog.Keys.First();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["productName"] = product,
            ["quantity"] = 1,
            ["customerId"] = $"CUST-AGS-{Guid.NewGuid():N}"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildOrderPlacementTool_ToolNotInList_BypassedAndCallsTool()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new()
        };
        var svc = Build(opts);
        var fn = svc.BuildOrderPlacementTool();
        var product = FakeDatabase.ProductCatalog.Keys.First();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["productName"] = product,
            ["quantity"] = 1,
            ["customerId"] = $"CUST-BYPASS-{Guid.NewGuid():N}"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildOrderPlacementTool_ApprovalRejected_ReturnsValidationError()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.OrderPlacement },
            TimeoutSeconds = 5
        };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);

        queue.RequestCreated += (_, req) =>
        {
            queue.Decide(req.Id, approved: false, decidedBy: "test", reason: "test_reject");
        };

        var svc = Build(opts, queue);
        var fn = svc.BuildOrderPlacementTool();
        var product = FakeDatabase.ProductCatalog.Keys.First();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["productName"] = product,
            ["quantity"] = 1,
            ["customerId"] = $"CUST-REJ-{Guid.NewGuid():N}"
        }), TestContext.Current.CancellationToken);
        var (success, message) = ParseResult(result);
        success.Should().BeFalse();
        message.Should().Contain("test_reject");
    }

    [Fact]
    public async Task BuildOrderPlacementTool_ApprovalApproved_ToolExecutes()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.OrderPlacement },
            TimeoutSeconds = 5
        };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        queue.RequestCreated += (_, req) =>
        {
            queue.Decide(req.Id, approved: true, decidedBy: "test", reason: "ok");
        };

        var svc = Build(opts, queue);
        var fn = svc.BuildOrderPlacementTool();
        var product = FakeDatabase.ProductCatalog.Keys.First();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["productName"] = product,
            ["quantity"] = 1,
            ["customerId"] = $"CUST-APR-{Guid.NewGuid():N}"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildComplaintRegistrationTool_ApprovalRejected_ReturnsValidationError()
    {
        var opts = new ApprovalOptions
        {
            Enabled = true,
            ToolsRequiringApproval = new() { WellKnown.ToolNames.ComplaintRegistration },
            TimeoutSeconds = 5
        };
        var queue = new InMemoryApprovalQueue(
            Options.Create(opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        queue.RequestCreated += (_, req) =>
            queue.Decide(req.Id, approved: false, decidedBy: "t", reason: "no_complaint");

        var svc = Build(opts, queue);
        var fn = svc.BuildComplaintRegistrationTool();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "ORD-1",
            ["complaintText"] = "yeterli uzunlukta bir şikayet metni var burada",
            ["customerId"] = "CUST-1990"
        }), TestContext.Current.CancellationToken);
        var (success, message) = ParseResult(result);
        success.Should().BeFalse();
        message.Should().Contain("no_complaint");
    }

    [Fact]
    public async Task BuildComplaintRegistrationTool_ApprovalDisabled_BypassesGate()
    {
        var opts = new ApprovalOptions { Enabled = false };
        var svc = Build(opts);
        var fn = svc.BuildComplaintRegistrationTool();

        var result = await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["orderId"] = "ORD-1",
            ["complaintText"] = $"şikayet metni unique {Guid.NewGuid()} buraya yazıldı",
            ["customerId"] = "CUST-1990"
        }), TestContext.Current.CancellationToken);
        var (success, _) = ParseResult(result);
        success.Should().BeTrue();
    }
}
