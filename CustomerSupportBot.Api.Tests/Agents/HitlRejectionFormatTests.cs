using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using NSubstitute;
using ChatResponse = Microsoft.Extensions.AI.ChatResponse;

namespace CustomerSupportBot.Api.Tests.Agents;

// K3 madde 2: FunctionInvokingChatClient'ın red edilen bir ApprovalRequiredAIFunction için
// gerçekten ürettiği metnin "Tool call invocation rejected. {reason}" formatında olduğunu,
// prompt seviyesinde değil framework/agent seviyesinde kilitleyen regresyon testi. order-agent.md
// ve complaint-agent.md bu formatı JSON gibi parse etmeye çalışmadan tanıyıp status="failed"
// üretecek şekilde güncellendi (bkz. ApprovalGateService.cs); bu test o varsayımın hâlâ doğru
// olduğunu ChatClientAgent + mock IChatClient ile, gerçek LLM çağrısı olmadan doğrular.
public class HitlRejectionFormatTests
{
    [Fact]
    public async Task RejectedApprovalRequiredTool_ProducesFrameworkRejectionString_AndNeverInvokesInnerTool()
    {
        var callCount = 0;
        var secondCallMessages = new List<ChatMessage>();
        var toolInvoked = false;

        var chatClient = Substitute.For<IChatClient>();
        chatClient
            .GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                {
                    var call = new FunctionCallContent("call-1", "order_cancel_tool",
                        new Dictionary<string, object?> { ["orderId"] = "1042" });
                    return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
                }

                secondCallMessages.AddRange(callInfo.Arg<IEnumerable<ChatMessage>>() ?? []);
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "tamam")));
            });

        var innerTool = AIFunctionFactory.Create(
            (string orderId) =>
            {
                toolInvoked = true;
                return "should never run";
            },
            name: "order_cancel_tool");
        var gatedTool = new ApprovalRequiredAIFunction(innerTool);

        var agent = new ChatClientAgent(chatClient, instructions: "test", name: "TestAgent", tools: [gatedTool]);
        var session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var firstResponse = await agent.RunAsync("siparişimi iptal et", session, cancellationToken: TestContext.Current.CancellationToken);

        var approvalRequest = firstResponse.Messages
            .SelectMany(m => m.Contents)
            .OfType<ToolApprovalRequestContent>()
            .Single();

        var approvalResponse = approvalRequest.CreateResponse(approved: false, reason: "musteri_vazgecti");

        await agent.RunAsync(
            new ChatMessage(ChatRole.User, [approvalResponse]),
            session,
            cancellationToken: TestContext.Current.CancellationToken);

        toolInvoked.Should().BeFalse("HITL reddedildiğinde gerçek tool hiç çalışmamalı");
        callCount.Should().Be(2);

        var rejectionResult = secondCallMessages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .Select(c => c.Result?.ToString())
            .FirstOrDefault(r => r is not null && r.Contains("rejected", StringComparison.OrdinalIgnoreCase));

        rejectionResult.Should().NotBeNull();
        rejectionResult.Should().StartWith("Tool call invocation rejected.");
        rejectionResult.Should().Contain("musteri_vazgecti");
    }
}
