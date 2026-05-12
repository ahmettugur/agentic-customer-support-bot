using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

public class ReasoningMessageBuilderTests
{
    private readonly PromptService _prompts = new(NullLogger<PromptService>.Instance);

    [Fact]
    public void Build_NoHistory_OnlySystemAndUser()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        var msgs = builder.Build("merhaba", session, history: null, new VerifiedEntities());

        msgs.Should().HaveCount(2);
        msgs[0].Role.Should().Be(ChatRole.System);
        msgs[1].Role.Should().Be(ChatRole.User);
        msgs[1].Text.Should().Be("merhaba");
    }

    [Fact]
    public void Build_WithHistory_IncludesHistoryBetween()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "geçmiş soru"),
            new(ChatRole.Assistant, "geçmiş cevap")
        };

        var msgs = builder.Build("yeni", session, history, new VerifiedEntities());

        msgs.Should().HaveCount(4);
        msgs[0].Role.Should().Be(ChatRole.System);
        msgs[1].Role.Should().Be(ChatRole.User);
        msgs[1].Text.Should().Be("geçmiş soru");
        msgs[3].Text.Should().Be("yeni");
    }

    [Fact]
    public void Build_VerifiedEntities_InjectedToSystemPrompt()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "CUST-1990";
        var verified = new VerifiedEntities
        {
            CustomerId = new VerifiedEntity { Value = "CUST-1990", Verification = EntityVerification.Verified }
        };

        var msgs = builder.Build("soru", session, null, verified);
        msgs[0].Text.Should().Contain("CUST-1990");
    }
}
