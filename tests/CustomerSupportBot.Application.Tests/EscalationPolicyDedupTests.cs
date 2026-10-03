// Tests/Services/EscalationPolicyDedupTests.cs
//
// EscalationPolicyService'in GetOpen kontrolü ile CreateAsync arasındaki yarış penceresi.
// Kontrol cache'e bakar ve atomik değildir; asıl dedup sink'tedir (Postgres'te unique index).
// Yarışı kaybeden çağrı sink'ten MEVCUT kaydı geri alır — o durumda yeni bir atama olmadığı
// için önerilen temsilcinin yükü artırılmamalıdır.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Application.Services.Routing;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class EscalationPolicyDedupTests
{
    private static readonly ApprovalOptions Opts = new() { EscalationEnabled = true };

    private static (EscalationPolicyService svc, IHumanAgentRegistry registry) Build(IEscalationSink sink)
    {
        var routing = Options.Create(new RoutingOptions
        {
            SeedAgents = new()
            {
                new HumanAgent
                {
                    Id = "alice", DisplayName = "Alice", Skills = new() { "complaint" },
                    Languages = new() { "tr" }, IsActive = true, MaxConcurrentLoad = 5
                }
            }
        });
        var registry = new InMemoryHumanAgentRegistry(routing);
        var router = new SkillsBasedRouter(registry, routing);
        return (new EscalationPolicyService(sink, Options.Create(Opts), router, registry), registry);
    }

    private static ReasoningTrace Trace(string sessionId) => new()
    {
        SessionId = sessionId,
        TraceId = "t1",
        Reasoning = new ReasoningResult { Intent = "şikayet" },
        SpecialistReasonings = new()
        {
            new()
            {
                AgentName = WellKnown.AgentNames.Complaint,
                PostToolReflection = new PostToolReflection
                {
                    Status = WellKnown.TaskStatuses.NeedsEscalation,
                    HandoffReason = "manuel inceleme"
                }
            }
        }
    };

    [Fact]
    public async Task LostCreateRace_DoesNotIncrementAgentLoad()
    {
        var winner = new EscalationRequest
        {
            Id = "winner", SessionId = "s1", AgentName = WellKnown.AgentNames.Complaint,
            SuggestedAgentId = "alice"
        };
        var sink = new RaceLosingSink(winner);
        var (svc, registry) = Build(sink);

        await svc.ProcessPendingEscalationsAsync(Trace("s1"), "şikayetim var", "yanıt",
            TestContext.Current.CancellationToken);

        sink.CreateCalls.Should().Be(1);
        registry.Get("alice")!.CurrentLoad.Should().Be(0,
            "kayıt yeni oluşmadı; yük kazanan çağrıda zaten bir kez artırıldı");
    }

    [Fact]
    public async Task WonCreate_IncrementsAgentLoadOnce()
    {
        var sink = new InMemoryEscalationSink(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<InMemoryEscalationSink>.Instance);
        var (svc, registry) = Build(sink);

        await svc.ProcessPendingEscalationsAsync(Trace("s1"), "şikayetim var", "yanıt",
            TestContext.Current.CancellationToken);

        sink.GetOpen().Should().ContainSingle();
        registry.Get("alice")!.CurrentLoad.Should().Be(1);
    }

    /// <summary>
    /// Kontrol anında açık kayıt görmeyen (cache henüz güncellenmemiş) ama INSERT'te kısıta
    /// takılıp kazananı döndüren sink — iki pod / iki paralel alt görev yarışının kaybeden tarafı.
    /// </summary>
    private sealed class RaceLosingSink(EscalationRequest winner) : IEscalationSink
    {
        public int CreateCalls { get; private set; }

        public Task<EscalationRequest> CreateAsync(EscalationRequest request)
        {
            CreateCalls++;
            return Task.FromResult(winner);
        }

        public IReadOnlyList<EscalationRequest> GetOpen() => [];
        public IReadOnlyList<EscalationRequest> GetRecent(int count = 50) => [];
        public EscalationRequest? Get(string id) => null;

        public Task<IReadOnlyList<EscalationRequest>> GetRecentForAgentAsync(
            string agentId, int count = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<EscalationRequest>>([]);

        public Task<bool> DecideAsync(string id, string action, string? assignedTo = null, string? resolution = null) =>
            Task.FromResult(false);

#pragma warning disable CS0067 // test ikizi — olaylar kullanılmıyor
        public event EventHandler<EscalationRequest>? RequestCreated;
        public event EventHandler<EscalationRequest>? RequestDecided;
#pragma warning restore CS0067
    }
}
