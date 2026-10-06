// Temsilci asistanı — devralınan sohbet için özet, bağlam ve yanıt taslağı.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class AgentAssistServiceTests
{
    private const string Sid = "assist-s1";

    private sealed class Harness
    {
        public required AgentAssistService Service { get; init; }
        public required InMemorySessionManager Sessions { get; init; }
        public required InMemoryCustomerProfileStore Profiles { get; init; }
        public required IMemoryPort Memory { get; init; }
        public required InMemoryEscalationSink Escalations { get; init; }
        public required InMemoryApprovalQueue Approvals { get; init; }
        public required IGeneralChatClient Llm { get; init; }
        public required List<IReadOnlyList<ConversationMessage>> LlmCalls { get; init; }
    }

    private static Harness Build(string llmReply = """{"summary":"Müşteri kargosu gecikti.","customerRequest":"Kargonun durumunu öğrenmek istiyor.","suggestedReply":"Merhaba, siparişinizi hemen kontrol ediyorum."}""")
    {
        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        var sessions = new InMemorySessionManager(locks);
        var profiles = new InMemoryCustomerProfileStore();
        var memory = Substitute.For<IMemoryPort>();
        memory.Enabled.Returns(true);
        memory.SearchAsync(Arg.Any<MemoryKind>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchHit>());
        var escalations = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        var approvals = new InMemoryApprovalQueue(
            Options.Create(new ApprovalOptions()), Substitute.For<IApprovalExecutionRouter>(),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(AgentAssistService.PromptKey).Returns("ASSIST SYSTEM PROMPT");

        var llmCalls = new List<IReadOnlyList<ConversationMessage>>();
        var llm = Substitute.For<IGeneralChatClient>();
        llm.CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>())
            .Returns(ci => { llmCalls.Add(ci.Arg<IReadOnlyList<ConversationMessage>>()); return llmReply; });

        return new Harness
        {
            Service = new AgentAssistService(
                sessions, profiles, memory, escalations, approvals, llm, prompts, new ContextSanitizer(),
                NullLogger<AgentAssistService>.Instance),
            Sessions = sessions, Profiles = profiles, Memory = memory,
            Escalations = escalations, Approvals = approvals, Llm = llm, LlmCalls = llmCalls
        };
    }

    private static async Task SeedAsync(Harness h, string? authCustomer = "1001", string? llmCustomer = null)
    {
        var session = await h.Sessions.GetOrCreateAsync(Sid, CancellationToken.None);
        await h.Sessions.AddExchangeAsync(Sid, "Siparişim 1030 hâlâ gelmedi", "Kontrol ediyorum.", ct: CancellationToken.None);
        await h.Sessions.AddExchangeAsync(Sid, "Çok kızgınım, temsilci istiyorum", "Sizi bir temsilciye aktarıyorum.", ct: CancellationToken.None);
        // AddExchangeAsync duyguyu kural tabanlı yeniden hesaplar — test değerleri SONRA yazılır.
        session.State.AuthenticatedCustomerId = authCustomer;
        session.State.CustomerId = llmCustomer;
        session.State.Sentiment = "negative";
        session.State.SentimentScore = 0.2;
        session.State.ConsecutiveNegativeTurns = 2;
    }

    [Fact]
    public async Task UnknownSession_ReturnsNull()
    {
        var h = Build();
        (await h.Service.GetAssistAsync("yok", CancellationToken.None)).Should().BeNull();
        await h.Llm.DidNotReceiveWithAnyArgs().CompleteAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ParsesSummaryRequestAndDraft_FromModelJson()
    {
        var h = Build();
        await SeedAsync(h);

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.Summary.Should().Be("Müşteri kargosu gecikti.");
        r.CustomerRequest.Should().Be("Kargonun durumunu öğrenmek istiyor.");
        r.SuggestedReply.Should().Be("Merhaba, siparişinizi hemen kontrol ediyorum.");
        r.AssistError.Should().BeNull();
        r.Sentiment.Should().Be(new AgentAssistSentiment("negative", 0.2, 2));
    }

    [Fact]
    public async Task ModelJsonInsideCodeFence_IsAccepted()
    {
        var h = Build("```json\n{\"summary\":\"Özet\",\"customerRequest\":\"Talep\",\"suggestedReply\":\"Taslak\"}\n```");
        await SeedAsync(h);

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.Summary.Should().Be("Özet");
        r.SuggestedReply.Should().Be("Taslak");
    }

    [Fact]
    public async Task ModelFailure_StillReturnsTheNonLlmSections()
    {
        var h = Build();
        await SeedAsync(h);
        await h.Profiles.UpsertAsync(new CustomerProfile { CustomerId = "1001", Summary = "Sadık müşteri" });
        h.Llm.CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("model down"));

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.Summary.Should().BeNull();
        r.SuggestedReply.Should().BeNull();
        r.AssistError.Should().NotBeNullOrWhiteSpace();
        r.Profile!.Summary.Should().Be("Sadık müşteri");
        r.Sentiment.Label.Should().Be("negative");
    }

    [Fact]
    public async Task UnparseableModelOutput_IsReportedAsAnError()
    {
        var h = Build("üzgünüm, yardımcı olamam");
        await SeedAsync(h);

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.SuggestedReply.Should().BeNull();
        r.AssistError.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Profile_IsTheAuthenticatedCustomers_NotTheOneTheModelExtracted()
    {
        var h = Build();
        await SeedAsync(h, authCustomer: "1001", llmCustomer: "2002");
        await h.Profiles.UpsertAsync(new CustomerProfile { CustomerId = "1001", Summary = "doğru müşteri" });
        await h.Profiles.UpsertAsync(new CustomerProfile { CustomerId = "2002", Summary = "BAŞKA müşteri" });

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.Profile!.CustomerId.Should().Be("1001");
        string.Concat(h.LlmCalls.Single().Select(m => m.Text)).Should().NotContain("BAŞKA müşteri");
    }

    [Fact]
    public async Task AnonymousSession_HasNoProfile()
    {
        var h = Build();
        await SeedAsync(h, authCustomer: null, llmCustomer: "2002");
        await h.Profiles.UpsertAsync(new CustomerProfile { CustomerId = "2002", Summary = "BAŞKA müşteri" });

        (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!.Profile.Should().BeNull();
    }

    [Fact]
    public async Task Articles_ComeFromKnowledgeSearchOnTheCustomersLatestMessages()
    {
        var h = Build();
        await SeedAsync(h);
        h.Memory.SearchAsync(MemoryKind.Knowledge, Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns([new MemorySearchHit
            {
                Document = new MemoryDocument { Title = "Kargo gecikmeleri", Text = "Kargo 3 iş günü içinde...", Source = "kargo.md" },
                Score = 0.82f
            }]);

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.Articles.Should().ContainSingle().Which.Title.Should().Be("Kargo gecikmeleri");
        await h.Memory.Received(1).SearchAsync(MemoryKind.Knowledge,
            Arg.Is<string>(q => q.Contains("temsilci istiyorum")), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MemoryDisabled_SkipsTheSearch()
    {
        var h = Build();
        h.Memory.Enabled.Returns(false);
        await SeedAsync(h);

        (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!.Articles.Should().BeEmpty();
        await h.Memory.DidNotReceiveWithAnyArgs().SearchAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SearchFailure_DoesNotBreakTheResult()
    {
        var h = Build();
        await SeedAsync(h);
        h.Memory.SearchAsync(Arg.Any<MemoryKind>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<MemorySearchHit>>(_ => throw new InvalidOperationException("qdrant down"));

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.Articles.Should().BeEmpty();
        r.SuggestedReply.Should().NotBeNull();
    }

    [Fact]
    public async Task OpenItems_AreThisSessionsOpenEscalationsAndPendingApprovals()
    {
        var h = Build();
        await SeedAsync(h);
        await h.Escalations.CreateAsync(new EscalationRequest { SessionId = Sid, AgentName = "OrderAgent", Reason = "Kargo kaybı şüphesi" });
        await h.Escalations.CreateAsync(new EscalationRequest { SessionId = "baska", AgentName = "OrderAgent", Reason = "başka oturum" });
        await h.Approvals.CreateAsync(new ApprovalRequest { SessionId = Sid, ToolName = WellKnown.ToolNames.OrderCancel, ParamSignature = "a" }, TestContext.Current.CancellationToken);
        await h.Approvals.CreateAsync(new ApprovalRequest { SessionId = "baska", ToolName = WellKnown.ToolNames.OrderCancel, ParamSignature = "b" }, TestContext.Current.CancellationToken);

        var r = (await h.Service.GetAssistAsync(Sid, CancellationToken.None))!;

        r.OpenItems.Select(i => (i.Kind, i.Description)).Should().BeEquivalentTo(new[]
        {
            ("escalation", "Kargo kaybı şüphesi"),
            ("approval", WellKnown.ToolNames.OrderCancel)
        });
    }

    [Fact]
    public async Task ConversationGoesToTheModel_AsFencedData_WithTheSystemPrompt()
    {
        var h = Build();
        await SeedAsync(h);

        await h.Service.GetAssistAsync(Sid, CancellationToken.None);

        var call = h.LlmCalls.Single();
        call[0].Role.Should().Be(ConversationRoles.System);
        call[0].Text.Should().Be("ASSIST SYSTEM PROMPT");
        var user = call.Last().Text;
        user.Should().Contain("Çok kızgınım, temsilci istiyorum");
        user.Should().Contain("<retrieved_data", "müşteri metni talimat değil veri olarak işaretlenmeli");
    }
}
