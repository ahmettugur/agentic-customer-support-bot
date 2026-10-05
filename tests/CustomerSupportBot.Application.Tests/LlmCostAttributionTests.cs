// Görüşme başına maliyet: sohbet turu dışındaki LLM yolları da görüşmeye atfedilmeli —
// fotoğraf analizi, temsilci asistanı, yeniden planlama.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Attachments;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Application.Services.Memory;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Application.Services.Telemetry;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class LlmCostAttributionTests
{
    private static InMemoryDistributedLock Locks() =>
        new(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));

    [Fact]
    public async Task PhotoAnalysis_IsAttributedToTheConversation()
    {
        var attribution = new LlmCallAttribution();
        var locks = Locks();
        var sessions = new InMemorySessionManager(locks);
        string? seen = "unset";
        var analyzer = Substitute.For<IImageAnalysisPort>();
        analyzer.DescribeAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { seen = attribution.CurrentSessionId; return "kupa"; });
        var guard = Substitute.For<IInputGuard>();
        guard.Inspect(Arg.Any<string>()).Returns(ci => new InputGuardResult(InputGuardVerdict.Allow, ci.Arg<string>() ?? "", [], null));
        var service = new ChatAttachmentService(new InMemoryAttachmentStore(), sessions, locks, analyzer, guard,
            Options.Create(new AttachmentOptions()), NullLogger<ChatAttachmentService>.Instance, attribution);

        var result = await service.UploadAsync(null, "1001", ChatAttachmentServiceTests.JpegWithExif());

        seen.Should().Be(result.SessionId);
    }

    [Fact]
    public async Task AgentAssist_IsAttributedToTheConversation()
    {
        var attribution = new LlmCallAttribution();
        var sessions = new InMemorySessionManager(Locks());
        var session = await sessions.GetOrCreateAsync("sess-assist");
        await sessions.AddExchangeAsync(session.SessionId, "kargom nerede", "bakıyorum");
        string? seen = "unset";
        var llm = Substitute.For<IGeneralChatClient>();
        llm.CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { seen = attribution.CurrentSessionId; return "{}"; });
        var memory = Substitute.For<IMemoryPort>();
        var prompts = Substitute.For<IPromptRepository>();
        prompts.Get(Arg.Any<string>()).Returns("prompt");
        var service = new AgentAssistService(sessions, new InMemoryCustomerProfileStore(), memory,
            new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance),
            new InMemoryApprovalQueue(Options.Create(new ApprovalOptions()), Substitute.For<IApprovalExecutionRouter>(),
                NullLogger<InMemoryApprovalQueue>.Instance),
            llm, prompts, new ContextSanitizer(), NullLogger<AgentAssistService>.Instance, attribution);

        await service.GetAssistAsync("sess-assist");

        seen.Should().Be("sess-assist");
    }

    [Fact]
    public async Task Replan_IsAttributedToTheConversation()
    {
        var attribution = new LlmCallAttribution();
        var sessions = new InMemorySessionManager(Locks());
        var session = await sessions.GetOrCreateAsync("sess-replan");
        await sessions.AddExchangeAsync(session.SessionId, "iade istiyorum", "tamam");
        var seen = new List<string?>();
        var reasoning = Substitute.For<IReasoningPort>();
        reasoning.ReasonAsync(Arg.Any<string>(), Arg.Any<AgentSession>(), Arg.Any<List<ConversationMessage>?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { seen.Add(attribution.CurrentSessionId); return new ReasoningResult(); });
        var team = Substitute.For<IAgentTeamPort>();
        team.RunAsync(Arg.Any<string>(), Arg.Any<List<ConversationMessage>?>(), Arg.Any<AgentSession?>(), Arg.Any<ReasoningResult?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { seen.Add(attribution.CurrentSessionId); return "yeniden planlandı"; });
        var service = new ReplanService(sessions, Substitute.For<IChatBridge>(), team, reasoning,
            Substitute.For<IApprovalContextAccessor>(), NullLogger<ReplanService>.Instance, attribution);

        await service.ExecuteAsync("sess-replan");

        seen.Should().Equal("sess-replan", "sess-replan");
    }
}
