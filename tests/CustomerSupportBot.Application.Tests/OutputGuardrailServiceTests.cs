// Servis katmanı: müşterinin kendi e-postasını/yazdıklarını bağlam olarak toplar, kapalıyken dokunmaz;
// ChatPortService hem akışlı hem akışsız yolda kanonik yanıtı bu kapıdan geçirir.

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

public class OutputGuardrailServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OutputGuardrailService Service(Action<OutputGuardrailOptions>? configure = null, string? email = "ayse@example.com")
    {
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(1001, Arg.Any<CancellationToken>()).Returns(email);
        var o = new OutputGuardrailOptions();
        configure?.Invoke(o);
        return new OutputGuardrailService(customers, Options.Create(o),
            Options.Create(new EmailOptions { FromAddress = "destek@sirket.com" }), NullLogger<OutputGuardrailService>.Instance);
    }

    [Fact]
    public async Task TheCustomersOwnEmail_FromTheRepository_IsAllowed()
    {
        var r = await Service().InspectAsync("Mail ayse@example.com adresine gitti", "sorgu", [], "1001", Ct);

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
    }

    [Fact]
    public async Task AnotherCustomersEmail_IsMasked_EvenForALoggedInCustomer()
    {
        var r = await Service().InspectAsync("Kayıt: baska@example.com", "sorgu", [], "1001", Ct);

        r.Verdict.Should().Be(OutputGuardVerdict.Redacted);
    }

    [Fact]
    public async Task EarlierUserMessages_CountAsKnownContacts()
    {
        var history = new List<ConversationMessage> { new("user", "mailim x@y.com") };

        var r = await Service().InspectAsync("x@y.com'a yazarız", "tamam", history, null, Ct);

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
    }

    [Fact]
    public async Task BotMessagesInHistory_DoNotLaunderAContact()
    {
        var history = new List<ConversationMessage> { new("assistant", "sizin için baska@example.com") };

        var r = await Service().InspectAsync("baska@example.com", "devam", history, null, Ct);

        r.Verdict.Should().Be(OutputGuardVerdict.Redacted, "botun daha önce söylediği, müşterinin yazdığı anlamına gelmez");
    }

    [Fact]
    public async Task TheConfiguredFromAddress_IsAnImplicitAllowedContact()
    {
        var r = await Service().InspectAsync("destek@sirket.com", "sorgu", [], null, Ct);

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
    }

    [Fact]
    public async Task WhenDisabled_NothingIsTouched()
    {
        var r = await Service(o => o.Enabled = false).InspectAsync("Password=abc baska@example.com", "q", [], null, Ct);

        r.Verdict.Should().Be(OutputGuardVerdict.Clean);
        r.Text.Should().Be("Password=abc baska@example.com");
    }

    [Fact]
    public async Task ARepositoryFailure_DoesNotBreakTheTurn_AndStillMasksUnknownContacts()
    {
        var customers = Substitute.For<ICustomerRepository>();
        customers.GetEmailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns<string?>(_ => throw new InvalidOperationException("db"));
        var svc = new OutputGuardrailService(customers, Options.Create(new OutputGuardrailOptions()),
            Options.Create(new EmailOptions()), NullLogger<OutputGuardrailService>.Instance);

        var r = await svc.InspectAsync("a@b.com", "q", [], "1001", Ct);

        r.Verdict.Should().Be(OutputGuardVerdict.Redacted);
    }

    // ── ChatPortService entegrasyonu ───────────────────────────────────────────

    private sealed class FakeTeam(string canonical, string rawDelta) : IAgentTeamPort
    {
        public async IAsyncEnumerable<StreamEvent> RunStreamingAsync(
            string query, List<ConversationMessage>? history = null, AgentSession? session = null, ReasoningResult? reasoning = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(rawDelta));
            yield return new StreamEvent(StreamEventTypes.ResponseComplete, new ResponseCompletePayload(canonical));
            await Task.CompletedTask;
        }

        public Task<string> RunAsync(string query, List<ConversationMessage>? history = null, AgentSession? session = null,
            ReasoningResult? reasoning = null, CancellationToken ct = default) => Task.FromResult(canonical);

        public string GetWorkflowDiagram() => "";
    }

    private sealed class NoopReasoning : IReasoningPort
    {
        public Task<ReasoningResult> ReasonAsync(string query, AgentSession? session = null,
            List<ConversationMessage>? history = null, CancellationToken ct = default) => Task.FromResult(new ReasoningResult());

        public async IAsyncEnumerable<StreamEvent> ReasonStreamingAsync(string query, AgentSession? session = null,
            List<ConversationMessage>? history = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield break;
        }
    }

    private static (ChatPortService Chat, List<string> Persisted) Chat(string canonical)
    {
        var session = new AgentSession { SessionId = "s1" };
        var sessions = Substitute.For<ISessionManager>();
        sessions.GetOrCreateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.ReloadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(session);
        sessions.GetHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new List<ConversationMessage>());
        var persisted = new List<string>();
        sessions.AddExchangeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Do<string>(persisted.Add),
            Arg.Any<TurnSignals?>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var modeRepo = Substitute.For<IChatModeRegistry>();
        modeRepo.GetMode(Arg.Any<string>()).Returns(ChatMode.Bot);

        var chat = new ChatPortService(new FakeTeam(canonical, canonical), new NoopReasoning(), sessions, modeRepo,
            Substitute.For<IChatBridge>(), new SessionStateService(sessions, NullLogger<SessionStateService>.Instance),
            Substitute.For<IApprovalContextAccessor>(),
            new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 })),
            NullLogger<ChatPortService>.Instance, guardrail: Service());
        return (chat, persisted);
    }

    [Fact]
    public async Task Streaming_TheCanonicalEventAndThePersistedAnswer_AreGuarded()
    {
        var (chat, persisted) = Chat("Başka müşterinin adresi baska@example.com");
        string? shown = null;

        await foreach (var e in chat.HandleStreamAsync(new ChatRequest("merhaba", "s1"), Ct))
            if (e.Type == StreamEventTypes.ResponseComplete) shown = ((ResponseCompletePayload)e.Data!).Text;

        shown.Should().NotContain("baska@example.com").And.Contain(OutputGuardrail.RedactedMark);
        persisted.Should().ContainSingle().Which.Should().NotContain("baska@example.com", "geçmişe ham metin yazılmamalı");
    }

    [Fact]
    public async Task NonStreaming_TheReturnedAnswer_IsGuarded()
    {
        var (chat, persisted) = Chat("Password=abc123");

        var response = await chat.HandleAsync(new ChatRequest("merhaba", "s1"), Ct);

        response.Response.Should().Be(OutputGuardrail.BlockedReply);
        persisted.Should().ContainSingle().Which.Should().Be(OutputGuardrail.BlockedReply, "geçmişe ham sızıntı yazılmamalı");
    }
}
