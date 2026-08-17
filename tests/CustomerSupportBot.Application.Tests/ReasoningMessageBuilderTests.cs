using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Application.Services.Reasoning;

namespace CustomerSupportBot.Application.Tests;

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
        session.State.AuthenticatedCustomerId = "1027";
        var verified = new VerifiedEntities
        {
            CustomerId = new VerifiedEntity { Value = "1027", Verification = EntityVerification.Verified }
        };

        var msgs = builder.Build("soru", session, null, verified);
        msgs[0].Text.Should().Contain("1027");
    }

    [Fact]
    public void Build_StateInfo_UsesAuthenticatedIdentity_NotLlmExtractedOne()
    {
        // STATE_INFO'daki "CustomerId:" satırı reasoning ajanının "kiminle konuşuyorum"
        // algısını kurar. LLM'in kullanıcı metninden çıkardığı (kullanıcının "ben 1008'im"
        // diyerek değiştirebildiği) State.CustomerId buraya sızarsa ajan yanlış kimlik
        // üzerinden akıl yürütür.
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        session.State.AuthenticatedCustomerId = "1027";
        session.State.CustomerId = "1008";

        var msgs = builder.Build("soru", session, null, new VerifiedEntities());

        // NOT: prompt ŞABLONU "1008"i ID formatı örneği olarak zaten içeriyor — bu yüzden
        // düz NotContain("1008") yanıltıcı olur; kontrol STATE_INFO satırına özel yapılır.
        msgs[0].Text.Should().Contain("CustomerId: 1027");
        msgs[0].Text.Should().NotContain("CustomerId: 1008");
    }
}
