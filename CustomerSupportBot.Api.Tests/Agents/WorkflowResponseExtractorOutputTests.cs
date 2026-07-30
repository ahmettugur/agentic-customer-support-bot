// Tests/Agents/WorkflowResponseExtractorOutputTests.cs
// WorkflowOutputEvent ile çalışan extractor metodları.
using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Tests.Agents;

public class WorkflowResponseExtractorOutputTests
{
    private static WorkflowOutputEvent Out(object data)
        => new(data, executorId: "TestExecutor");

    private static ChatMessage Msg(ChatRole role, string text, string? authorName = null)
        => new(role, text) { AuthorName = authorName };

    // ─── ExtractResultFromOutput ───

    [Fact]
    public void ExtractResultFromOutput_StringData_ReturnsString()
    {
        WorkflowResponseExtractor.ExtractResultFromOutput(Out("hello"))
            .Should().Be("hello");
    }

    [Fact]
    public void ExtractResultFromOutput_SingleChatMessage_ReturnsText()
    {
        var msg = new ChatMessage(ChatRole.Assistant, "merhaba");
        WorkflowResponseExtractor.ExtractResultFromOutput(Out(msg))
            .Should().Be("merhaba");
    }

    [Fact]
    public void ExtractResultFromOutput_UnknownData_ReturnsEmpty()
    {
        WorkflowResponseExtractor.ExtractResultFromOutput(Out(42))
            .Should().Be("");
    }

    [Fact]
    public void ExtractResultFromOutput_TerminateMessage_PreferredOverLastAssistant()
    {
        var messages = new List<ChatMessage>
        {
            Msg(ChatRole.User, "soru"),
            Msg(ChatRole.Assistant, "ara yanıt", WellKnown.AgentNames.Planning),
            Msg(ChatRole.Assistant, "son cevap TERMINATE", WellKnown.AgentNames.Response),
            Msg(ChatRole.Assistant, "sonradan eklenen", WellKnown.AgentNames.Response),
        };
        WorkflowResponseExtractor.ExtractResultFromOutput(Out(messages))
            .Should().Contain("TERMINATE");
    }

    [Fact]
    public void ExtractResultFromOutput_NoTerminate_ReturnsLastNonRoutingAssistant()
    {
        var messages = new List<ChatMessage>
        {
            Msg(ChatRole.User, "soru"),
            Msg(ChatRole.Assistant, "OrderAgent: lütfen sipariş id verin", WellKnown.AgentNames.Planning),
            Msg(ChatRole.Assistant, "sipariş işleniyor durumdadır", WellKnown.AgentNames.Order),
        };
        WorkflowResponseExtractor.ExtractResultFromOutput(Out(messages))
            .Should().Be("sipariş işleniyor durumdadır");
    }

    [Fact]
    public void ExtractResultFromOutput_OnlyRoutingMessages_FallsBackToLastAssistant()
    {
        var messages = new List<ChatMessage>
        {
            Msg(ChatRole.Assistant, "OrderAgent: yardım", WellKnown.AgentNames.Planning),
        };
        WorkflowResponseExtractor.ExtractResultFromOutput(Out(messages))
            .Should().Contain("OrderAgent");
    }

    [Fact]
    public void ExtractResultFromOutput_EmptyMessages_ReturnsEmpty()
    {
        WorkflowResponseExtractor.ExtractResultFromOutput(Out(new List<ChatMessage>()))
            .Should().Be("");
    }

    // ExtractPlanningFromOutput

    [Fact]
    public void ExtractPlanningFromOutput_NotChatMessages_ReturnsNull()
    {
        WorkflowResponseExtractor.ExtractPlanningFromOutput(Out(123))
            .Should().BeNull();
    }

    [Fact]
    public void ExtractPlanningFromOutput_NoPlanningMessage_ReturnsNull()
    {
        var messages = new List<ChatMessage>
        {
            Msg(ChatRole.User, "merhaba"),
        };
        WorkflowResponseExtractor.ExtractPlanningFromOutput(Out(messages))
            .Should().BeNull();
    }

    [Fact]
    public void ExtractPlanningFromOutput_PlanningAgentMessage_Parses()
    {
        var planningJson = """
        ```json
        {
          "detectedIntent": "order_inquiry",
          "intentConfidence": 0.9,
          "selectedAgent": "OrderAgent",
          "needsClarification": false
        }
        ```
        """;
        var messages = new List<ChatMessage>
        {
            Msg(ChatRole.Assistant, planningJson, WellKnown.AgentNames.Planning),
        };
        var result = WorkflowResponseExtractor.ExtractPlanningFromOutput(Out(messages));
        result.Should().NotBeNull();
        result.SelectedAgent.Should().Be("OrderAgent");
    }

    // ─── ExtractSpecialistReasoningsFromOutput ───

    [Fact]
    public void ExtractSpecialistReasoningsFromOutput_NotChatMessages_ReturnsEmpty()
    {
        WorkflowResponseExtractor.ExtractSpecialistReasoningsFromOutput(Out("string"))
            .Should().BeEmpty();
    }

    [Fact]
    public void ExtractSpecialistReasoningsFromOutput_NoSpecialistJson_ReturnsEmpty()
    {
        var messages = new List<ChatMessage>
        {
            Msg(ChatRole.Assistant, "düz yanıt", WellKnown.AgentNames.Order),
        };
        WorkflowResponseExtractor.ExtractSpecialistReasoningsFromOutput(Out(messages))
            .Should().BeEmpty();
    }

    [Fact]
    public void ExtractSpecialistReasoningsFromOutput_NoAuthor_Skipped()
    {
        var json = """{"preToolCheck": {"canProceed": true}}""";
        var messages = new List<ChatMessage>
        {
            Msg(ChatRole.Assistant, json, authorName: null),
        };
        WorkflowResponseExtractor.ExtractSpecialistReasoningsFromOutput(Out(messages))
            .Should().BeEmpty();
    }

    // ContainsAgentRoutingMessage edge cases 

    [Fact]
    public void ContainsAgentRoutingMessage_PlanningName_True()
    {
        WorkflowResponseExtractor.ContainsAgentRoutingMessage("PlanningAgent decided")
            .Should().BeTrue();
    }

    [Fact]
    public void ContainsAgentRoutingMessage_CaseInsensitive_True()
    {
        WorkflowResponseExtractor.ContainsAgentRoutingMessage("orderagent içerik")
            .Should().BeTrue();
    }

    // ─── IsInternalWorkflowExecutor ───

    [Theory]
    [InlineData("GroupChatHost-1")]
    [InlineData("GroupChatManager")]
    [InlineData("RoundRobinGroupChatManager-x")]
    [InlineData("StartExecutor")]
    [InlineData("EndExecutor-end")]
    public void IsInternalWorkflowExecutor_KnownPrefixes_True(string id)
    {
        WorkflowResponseExtractor.IsInternalWorkflowExecutor(id).Should().BeTrue();
    }

    [Theory]
    [InlineData("PlanningAgent")]
    [InlineData("OrderAgent")]
    [InlineData("CustomAgent")]
    public void IsInternalWorkflowExecutor_UserAgent_False(string id)
    {
        WorkflowResponseExtractor.IsInternalWorkflowExecutor(id).Should().BeFalse();
    }

    // ─── RemoveTechnicalJsonBlocks ───

    [Fact]
    public void RemoveTechnicalJsonBlocks_PlainJsonWithSelfCritique_Stripped()
    {
        var input = """
            Cevap hazır.
            {"selfCritique": {"rating": "good"}}
            """;
        var result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(input);
        result.Should().NotContain("selfCritique");
        result.Should().Contain("Cevap hazır");
    }

    [Fact]
    public void RemoveTechnicalJsonBlocks_CollapsesMultipleNewlines()
    {
        var input = "Bir\n\n\n\n\nki";
        var result = WorkflowResponseExtractor.RemoveTechnicalJsonBlocks(input);
        result.Should().NotContain("\n\n\n");
    }

    [Fact]
    public void RemoveTechnicalJsonBlocks_EmptyInput_ReturnsEmpty()
    {
        WorkflowResponseExtractor.RemoveTechnicalJsonBlocks("").Should().Be("");
    }

    // ExtractDeltaText

    [Fact]
    public void ExtractDeltaText_ObjectWithoutTextProperty_ReturnsEmpty()
    {
        var data = new { other = "x" };
        WorkflowResponseExtractor.ExtractDeltaText(data).Should().Be("");
    }

    [Fact]
    public void ExtractDeltaText_TextNull_ReturnsEmpty()
    {
        var data = new { text = (string?)null };
        WorkflowResponseExtractor.ExtractDeltaText(data).Should().Be("");
    }

    // StreamTextInChunksAsync 

    [Fact]
    public async Task StreamTextInChunksAsync_EmptyText_NoChunks()
    {
        var chunks = new List<string>();
        await foreach (var c in WorkflowResponseExtractor.StreamTextInChunksAsync("", CancellationToken.None))
            chunks.Add(c);
        chunks.Should().BeEmpty();
    }

    [Fact]
    public async Task StreamTextInChunksAsync_TextWithSpaces_ChunksByWord()
    {
        var chunks = new List<string>();
        await foreach (var c in WorkflowResponseExtractor.StreamTextInChunksAsync("ab cd ef", CancellationToken.None))
            chunks.Add(c);

        string.Concat(chunks).Should().Be("ab cd ef");
        chunks.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task StreamTextInChunksAsync_NoSpaces_SingleChunk()
    {
        var chunks = new List<string>();
        await foreach (var c in WorkflowResponseExtractor.StreamTextInChunksAsync("abcdef", CancellationToken.None))
            chunks.Add(c);
        chunks.Should().HaveCount(1);
        chunks[0].Should().Be("abcdef");
    }

    [Fact]
    public async Task StreamTextInChunksAsync_Cancelled_StopsEarly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var chunks = new List<string>();
        var act = async () =>
        {
            await foreach (var c in WorkflowResponseExtractor.StreamTextInChunksAsync("a b c d e", cts.Token))
                chunks.Add(c);
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
