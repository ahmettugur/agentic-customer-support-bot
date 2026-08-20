// Bileşik (compound) bir turun KAÇ tur sayıldığı.
//
// Tek bir kullanıcı mesajı N alt göreve bölündüğünde, her alt görev ayrı bir workflow koşusudur.
// Tur bazlı yan etkiler (episodic bellek kaydı, müşteri profili etkileşim sayacı) her koşuda
// tekrarlanırsa profil N tur ilerler ve birbirinden kopuk N sentetik episode yazılır. Episodic
// bellek sonradan ARANAN bir kaynak olduğu için bu, kalıcı olarak kirlenmiş bellek demektir.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class CompoundTurnFinalizationTests
{
    private sealed class RecordingMemory : ISemanticMemoryWriter
    {
        public bool Enabled => true;
        public List<string> Episodes { get; } = [];

        public Task WriteEpisodeAsync(string sessionId, string traceId, string userQuery,
            string finalResponse, string? intent, int? rating, string? customerId = null,
            CancellationToken ct = default)
        {
            Episodes.Add(userQuery);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingProfile : ICustomerProfileService
    {
        public int Interactions { get; private set; }

        public Task<CustomerProfile?> RecordInteractionAsync(
            string? customerId, string userQuery, string botResponse, string? intent,
            int? rating = null, bool isNewSession = false, CancellationToken ct = default)
        {
            Interactions++;
            return Task.FromResult<CustomerProfile?>(null);
        }
    }

    private static TurnFinalizer Build(RecordingMemory memory, CountingProfile profile)
    {
        var approvalOpts = Options.Create(new ApprovalOptions { Enabled = false });
        var sink = Substitute.For<IEscalationSink>();
        var approvalGate = new ApprovalGateService(
            Substitute.For<IApprovalQueue>(), approvalOpts, sink,
            Substitute.For<IApprovalContextAccessor>(),
            Substitute.For<ICustomerSupportToolsService>(),
            new EscalationPolicyService(sink, approvalOpts));

        return new TurnFinalizer(
            Substitute.For<IReasoningTraceStore>(), approvalGate,
            NullLoggerFactory.Instance, memory, profile);
    }

    private static AgentSession Session() => new()
    {
        SessionId = "s1",
        State = new SessionState { AuthenticatedCustomerId = "1027" }
    };

    /// <summary>
    /// Alt görev koşusu tur bazlı yan etki YAZMAZ. Sinyal, decompose sırasında doldurulan
    /// <c>ConstrainedTargetAgent</c>'tır (bkz. WorkflowRunner).
    /// </summary>
    [Fact]
    public async Task SubTaskRun_WritesNoEpisodeAndDoesNotAdvanceProfile()
    {
        var memory = new RecordingMemory();
        var profile = new CountingProfile();
        var trace = new ReasoningTrace { TraceId = "t1", SessionId = "s1" };

        await Build(memory, profile).FinalizeAsync(
            trace, Session(), "sipariş 1030 durumu", "Teslim edildi.",
            WellKnown.Termination.ReasonCompleted,
            TestContext.Current.CancellationToken, isSubTaskRun: true);

        memory.Episodes.Should().BeEmpty("alt görevler ayrı bir tur değildir");
        profile.Interactions.Should().Be(0);
    }

    /// <summary>Tekil (bölünmemiş) tur eskisi gibi davranmalı — düzeltme onu kapatmamalı.</summary>
    [Fact]
    public async Task NormalRun_StillWritesEpisodeAndAdvancesProfile()
    {
        var memory = new RecordingMemory();
        var profile = new CountingProfile();
        var trace = new ReasoningTrace { TraceId = "t1", SessionId = "s1" };

        await Build(memory, profile).FinalizeAsync(
            trace, Session(), "sipariş 1030 durumu", "Teslim edildi.",
            WellKnown.Termination.ReasonCompleted,
            TestContext.Current.CancellationToken);

        await WaitForEpisodeAsync(memory);
        memory.Episodes.Should().ContainSingle();
        profile.Interactions.Should().Be(1);
    }

    /// <summary>
    /// Birleşik tur bir KEZ yazılır ve kaydedilen metin, alt görev parçası değil kullanıcının
    /// asıl mesajıdır.
    /// </summary>
    [Fact]
    public async Task AggregateTurn_WritesExactlyOneEpisodeWithTheOriginalQuery()
    {
        var memory = new RecordingMemory();
        var profile = new CountingProfile();

        await Build(memory, profile).FinalizeAggregateTurnAsync(
            Session(), "1030 nerede ve şikayetim ne oldu?", "Birleşik yanıt.",
            WellKnown.Intents.OrderInquiry, TestContext.Current.CancellationToken);

        await WaitForEpisodeAsync(memory);
        memory.Episodes.Should().ContainSingle()
            .Which.Should().Be("1030 nerede ve şikayetim ne oldu?");
        profile.Interactions.Should().Be(1);
    }

    /// <summary>Episodic yazım bilinçli olarak fire-and-forget; testin onu beklemesi gerekir.</summary>
    private static async Task WaitForEpisodeAsync(RecordingMemory memory)
    {
        for (var i = 0; i < 50 && memory.Episodes.Count == 0; i++)
            await Task.Delay(20);
    }
}
