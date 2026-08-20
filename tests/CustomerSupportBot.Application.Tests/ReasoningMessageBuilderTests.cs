using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Persistence.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Application.Services.Reasoning;

namespace CustomerSupportBot.Application.Tests;

public class ReasoningMessageBuilderTests
{
    /// <summary>Bu testlerin konusu geçmiş kırpma değil; sınır kısıtlamayacak kadar yüksek.</summary>
    private const int DefaultHistoryCap = 100;

    private readonly FileSystemPromptRepository _prompts = new(NullLogger<FileSystemPromptRepository>.Instance);

    [Fact]
    public void Build_NoHistory_OnlySystemAndUser()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        var msgs = builder.Build(DefaultHistoryCap, "merhaba", session, history: null, new VerifiedEntities());

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

        var msgs = builder.Build(DefaultHistoryCap, "yeni", session, history, new VerifiedEntities());

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

        var msgs = builder.Build(DefaultHistoryCap, "soru", session, null, verified);
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

        var msgs = builder.Build(DefaultHistoryCap, "soru", session, null, new VerifiedEntities());

        // NOT: prompt ŞABLONU "1008"i ID formatı örneği olarak zaten içeriyor — bu yüzden
        // düz NotContain("1008") yanıltıcı olur; kontrol STATE_INFO satırına özel yapılır.
        msgs[0].Text.Should().Contain("CustomerId: 1027");
        msgs[0].Text.Should().NotContain("CustomerId: 1008");
    }

    // ═══ Geçmiş bütçesi ═══

    /// <summary>
    /// Reasoning'e YALNIZCA yakın geçmiş gider.
    ///
    /// <para>
    /// Workflow tarafında geçmiş özetlenip kırpılıyordu ama reasoning oturumun tamamını
    /// gönderiyordu. Uzun oturumlarda bu token maliyetini, gecikmeyi ve bağlam sınırını aşma
    /// riskini birlikte büyütür — sınır aşılırsa reasoning fallback'e düşer ve tur sessizce
    /// kalitesizleşir.
    /// </para>
    /// </summary>
    [Fact]
    public void Build_WithLongHistory_SendsOnlyTheMostRecentMessages()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        var history = Enumerable.Range(1, 40)
            .Select(i => new ConversationMessage(ConversationRoles.User, $"mesaj-{i}"))
            .ToList();

        var msgs = builder.Build(6, "yeni soru", session, history, new VerifiedEntities());

        // en yeni 6 geçmiş mesajı + güncel sorgu (sistem mesajı hariç)
        msgs.Count(m => m.Role != ConversationRoles.System).Should().Be(7);
        msgs.Should().Contain(m => m.Text == "mesaj-40", "en yeni tur korunmalı");
        msgs.Should().NotContain(m => m.Text == "mesaj-1", "en eski turlar kırpılmalı");
    }

    /// <summary>Sınırın altındaki geçmiş olduğu gibi gitmeli — kırpma gereksiz yere devreye girmemeli.</summary>
    [Fact]
    public void Build_WithShortHistory_SendsEverything()
    {
        var builder = new ReasoningMessageBuilder(_prompts);
        var session = new AgentSession { SessionId = "s1" };
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "bir"),
            new(ConversationRoles.Assistant, "iki")
        };

        var msgs = builder.Build(12, "üç", session, history, new VerifiedEntities());

        // 2 geçmiş + güncel sorgu
        msgs.Count(m => m.Role != ConversationRoles.System).Should().Be(3);
    }
}
