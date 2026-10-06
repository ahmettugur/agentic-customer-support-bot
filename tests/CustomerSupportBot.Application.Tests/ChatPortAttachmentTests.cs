// Mesajla gönderilen fotoğraf kimlikleri sohbet turuna doğru yansıyor mu?
// Ajan analiz metnini görmeli, geçmişe de aynı metin yazılmalı; başkasının fotoğrafı yok sayılmalı.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ChatPortAttachmentTests
{
    private sealed class CapturingTeam : IAgentTeamPort
    {
        public string? Query { get; private set; }

        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
            string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Query = query;
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload("Tamam."));
            await Task.CompletedTask;
        }

        public Task<string> RunAsync(string query, List<ConversationMessage>? history = null,
            AgentSession? session = null, ReasoningResult? reasoning = null, CancellationToken ct = default)
        {
            Query = query;
            return Task.FromResult("Tamam.");
        }

        public string GetWorkflowDiagram() => "";
    }

    private sealed class NoopReasoning : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string query, AgentSession? session = null,
            List<ConversationMessage>? history = null, CancellationToken ct = default)
            => Task.FromResult(new ReasoningResult());

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(
            string query, AgentSession? session = null, List<ConversationMessage>? history = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
    }

    private sealed record Fixture(
        ChatPortService Service, CapturingTeam Team, Func<string?> PersistedUser,
        InMemoryAttachmentStore Store, string MineId, string ForeignId);

    private static async Task<Fixture> CreateAsync()
    {
        var session = new AgentSession { SessionId = "s1" };
        session.State.AuthenticatedCustomerId = "1001";

        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<ConversationMessage>());
        string? persistedUser = null;
        await sessions.AddExchangeAsync(
            Arg.Any<string>(), Arg.Do<string>(q => persistedUser = q), Arg.Any<string>(),
            Arg.Any<TurnSignals?>(), Arg.Any<CancellationToken>());

        var modeRepo = Substitute.For<IChatModeRegistry>();
        modeRepo.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);

        var store = new InMemoryAttachmentStore();
        var mine = new ChatAttachment { SessionId = "s1", CustomerId = "1001", ContentType = "image/jpeg", Description = "Kulpu kırık beyaz kupa." };
        var foreign = new ChatAttachment { SessionId = "s9", CustomerId = "2002", ContentType = "image/jpeg", Description = "GİZLİ" };
        await store.SaveAsync(mine);
        await store.SaveAsync(foreign);

        var team = new CapturingTeam();
        var svc = new ChatPortService(
            team, new NoopReasoning(), sessions, modeRepo,
            Substitute.For<IChatBridge>(),
            new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance,
            store,
            Options.Create(new AttachmentOptions()));

        return new Fixture(svc, team, () => persistedUser, store, mine.Id, foreign.Id);
    }

    [Fact]
    public async Task Stream_AgentAndHistory_SeeTheOwnedPhotoAnalysis_ButNotForeignOnes()
    {
        var f = await CreateAsync();

        await foreach (var _ in f.Service.HandleStreamAsync(new ChatRequest("Ürün kırık geldi", "s1", "1001", [f.MineId, f.ForeignId]), TestContext.Current.CancellationToken)) { }

        f.Team.Query.Should().Contain("Ürün kırık geldi").And.Contain("Kulpu kırık beyaz kupa.").And.NotContain("GİZLİ");
        f.PersistedUser().Should().Be(f.Team.Query, "fotoğraf notu sonraki turlarda da görünmeli");
        (await f.Store.GetAsync(f.MineId, TestContext.Current.CancellationToken))!.SentAt.Should().NotBeNull("onay kapısı yalnızca gönderilmiş fotoğrafı bağlar");
        (await f.Store.GetAsync(f.ForeignId, TestContext.Current.CancellationToken))!.SentAt.Should().BeNull();
    }

    [Fact]
    public async Task NonStream_AgentSeesThePhotoAnalysis()
    {
        var f = await CreateAsync();

        await f.Service.HandleAsync(new ChatRequest("bak", "s1", "1001", [f.MineId]), TestContext.Current.CancellationToken);

        f.Team.Query.Should().Contain("[Müşterinin eklediği fotoğraf — otomatik analiz]: Kulpu kırık beyaz kupa.");
    }

    [Fact]
    public async Task WithoutAttachmentIds_QueryIsUnchanged()
    {
        var f = await CreateAsync();

        await f.Service.HandleAsync(new ChatRequest("merhaba", "s1", "1001"), TestContext.Current.CancellationToken);

        f.Team.Query.Should().Be("merhaba");
    }
}
