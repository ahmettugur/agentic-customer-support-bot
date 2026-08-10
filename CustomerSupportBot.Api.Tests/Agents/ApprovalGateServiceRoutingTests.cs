// Tests/Agents/ApprovalGateServiceRoutingTests.cs
// Smart Routing entegrasyon testleri — ApprovalGateService ProcessPendingEscalations
// çağrısı sonrası EscalationRequest'in routing alanlarının doğru doldurulduğunu doğrular.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Agents;

[Collection("PostgresCatalog")]
public class ApprovalGateServiceRoutingTests
{
    private readonly PostgresCatalogFixture _fixture;
    private readonly InMemoryEscalationSink _sink = new(NullLogger<InMemoryEscalationSink>.Instance);
    private readonly ApprovalOptions _opts = new() { EscalationEnabled = true };

    public ApprovalGateServiceRoutingTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    private (ApprovalGateService svc, IHumanAgentRegistry registry, ICustomerProfileStore profiles, ISessionManager sessions)
        BuildWithRouting(RoutingOptions? routingOpts = null)
    {
        var queue = new InMemoryApprovalQueue(Options.Create(_opts), new NoopApprovalExecutionRouter(), NullLogger<InMemoryApprovalQueue>.Instance);
        var routingWrapper = Options.Create(routingOpts ?? new RoutingOptions
        {
            IntentSkillMap = new(StringComparer.OrdinalIgnoreCase)
            {
                ["şikayet"] = new() { "complaint" }
            },
            SeedAgents = new()
            {
                new HumanAgent
                {
                    Id = "alice",
                    DisplayName = "Alice",
                    Skills = new() { "complaint", "refund" },
                    Languages = new() { "tr" },
                    IsActive = true,
                    MaxConcurrentLoad = 5
                }
            }
        });
        var registry = new InMemoryHumanAgentRegistry(routingWrapper);
        var router = new SkillsBasedRouter(registry, routingWrapper);
        var profiles = new InMemoryCustomerProfileStore();
        var distributedLock = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        var sessions = new InMemorySessionManager(distributedLock);

        var svc = new ApprovalGateService(
            queue,
            Options.Create(_opts),
            _sink,
            new ApprovalContextAccessor(),
            TestFactory.CreateToolsService(_fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo),
            new EscalationPolicyService(
                _sink, Options.Create(_opts), router, registry, profiles, sessions));

        return (svc, registry, profiles, sessions);
    }

    private static ReasoningTrace TraceFor(string sessionId, string intent, string agentName) => new()
    {
        SessionId = sessionId,
        TraceId = "t1",
        Reasoning = new ReasoningResult { Intent = intent },
        SpecialistReasonings = new()
        {
            new()
            {
                AgentName = agentName,
                PostToolReflection = new PostToolReflection
                {
                    Status = WellKnown.TaskStatuses.NeedsEscalation,
                    HandoffReason = "manuel inceleme"
                }
            }
        }
    };

    [Fact]
    public async Task Routing_AssignsBestMatchedAgentAndIncrementsLoad()
    {
        var (svc, registry, _, sessions) = BuildWithRouting();
        var session = await sessions.GetOrCreateAsync("s1", TestContext.Current.CancellationToken);
        session.State.AuthenticatedCustomerId = "1001";

        await svc.ProcessPendingEscalationsAsync(TraceFor("s1", "şikayet", WellKnown.AgentNames.Complaint),
            "şikayetim var", "yanıt");

        var open = _sink.GetOpen();
        open.Should().ContainSingle();
        open[0].SuggestedAgentId.Should().Be("alice");
        open[0].SuggestedAgentName.Should().Be("Alice");
        open[0].MatchScore.Should().BeGreaterThan(0);
        open[0].RequiredSkills.Should().Contain("complaint");
        open[0].Priority.Should().Be(EscalationPriority.High); // ComplaintAgent — High

        registry.Get("alice")!.CurrentLoad.Should().Be(1);
    }

    [Fact]
    public async Task Routing_CustomerProfileVipFlag_FeedsIntoSkills()
    {
        var routingOpts = new RoutingOptions
        {
            IntentSkillMap = new(StringComparer.OrdinalIgnoreCase) { ["şikayet"] = new() { "complaint" } },
            ProfileKeywordSkillMap = new(StringComparer.OrdinalIgnoreCase) { ["VIP"] = "vip" },
            SeedAgents = new()
            {
                new HumanAgent { Id = "generic", DisplayName = "G", Skills = new(){"complaint"}, Languages = new(){"tr"}, IsActive = true, MaxConcurrentLoad=5 },
                new HumanAgent { Id = "vip-handler", DisplayName = "V", Skills = new(){"complaint","vip"}, Languages = new(){"tr"}, IsActive = true, MaxConcurrentLoad=5 }
            }
        };
        var (svc, _, profiles, sessions) = BuildWithRouting(routingOpts);
        var session = await sessions.GetOrCreateAsync("s2", TestContext.Current.CancellationToken);
        session.State.AuthenticatedCustomerId = "9011";

        profiles.Upsert(new CustomerSupportBot.Domain.Model.Memory.CustomerProfile
        {
            CustomerId = "9011",
            PreferredLanguage = "tr",
            AdminNote = "VIP müşteri"
        });

        await svc.ProcessPendingEscalationsAsync(TraceFor("s2", "şikayet", WellKnown.AgentNames.Complaint),
            "şikayet", "yanıt");

        var open = _sink.GetOpen();
        open[0].SuggestedAgentId.Should().Be("vip-handler");
        open[0].RequiredSkills.Should().Contain("vip");
    }

    [Fact]
    public async Task Routing_NoActiveAgents_SuggestedAgentIdIsNull()
    {
        var routingOpts = new RoutingOptions { SeedAgents = new() }; // boş
        var (svc, _, _, sessions) = BuildWithRouting(routingOpts);
        (await sessions.GetOrCreateAsync("s3", TestContext.Current.CancellationToken)).State.AuthenticatedCustomerId = "C";

        await svc.ProcessPendingEscalationsAsync(TraceFor("s3", "şikayet", WellKnown.AgentNames.Complaint),
            "test", "yanıt");

        var open = _sink.GetOpen();
        open.Should().ContainSingle();
        open[0].SuggestedAgentId.Should().BeNull();
        open[0].RoutingNote.Should().NotBeNullOrWhiteSpace();
        open[0].Priority.Should().Be(EscalationPriority.High);
    }
}
