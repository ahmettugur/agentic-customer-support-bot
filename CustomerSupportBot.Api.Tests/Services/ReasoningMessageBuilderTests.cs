using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

public class ReasoningMessageBuilderTests
{
    private readonly FileSystemPromptRepository _prompts = new(NullLogger<FileSystemPromptRepository>.Instance);

    [Fact]
    public void Build_NoHistory_OnlySystemAndUser()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        var msgs = builder.Build("merhaba", session, history: null, new VerifiedEntities());

        msgs.Should().HaveCount(2);
        msgs[0].Role.Should().Be(ConversationRoles.System);
        msgs[1].Role.Should().Be(ConversationRoles.User);
        msgs[1].Text.Should().Be("merhaba");
    }

    [Fact]
    public void Build_WithHistory_IncludesHistoryBetween()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "geçmiş soru"),
            new(ConversationRoles.Assistant, "geçmiş cevap")
        };

        var msgs = builder.Build("yeni", session, history, new VerifiedEntities());

        msgs.Should().HaveCount(4);
        msgs[0].Role.Should().Be(ConversationRoles.System);
        msgs[1].Role.Should().Be(ConversationRoles.User);
        msgs[1].Text.Should().Be("geçmiş soru");
        msgs[3].Text.Should().Be("yeni");
    }

    [Fact]
    public void Build_VerifiedEntities_InjectedToSystemPrompt()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        session.State.CustomerId = "1027";
        var verified = new VerifiedEntities
        {
            CustomerId = new VerifiedEntity { Value = "1027", Verification = EntityVerification.Verified }
        };

        var msgs = builder.Build("soru", session, null, verified);
        msgs[0].Text.Should().Contain("1027");
    }
}
