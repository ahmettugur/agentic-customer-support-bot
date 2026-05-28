using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Tests.Agents;

[Collection("PostgresCatalog")]
public class ApprovalGateServiceEscalationTests
{
    private readonly PostgresCatalogFixture _fixture;
    private readonly InMemoryEscalationSink _sink = new(NullLogger<InMemoryEscalationSink>.Instance);
    private readonly ApprovalOptions _opts = new() { EscalationEnabled = true };

    public ApprovalGateServiceEscalationTests(PostgresCatalogFixture fixture)
    {
        _fixture = fixture;
    }

    private ApprovalGateService BuildService()
    {
        var queue = new InMemoryApprovalQueue(
            Options.Create(_opts),
            NullLogger<InMemoryApprovalQueue>.Instance);
        var escalationPolicy = new EscalationPolicyService(
            _sink, Options.Create(_opts));
        return new ApprovalGateService(
            queue,
            Options.Create(_opts),
            _sink,
            new ApprovalContextAccessor(),
            TestFactory.CreateToolsService(_fixture.ProductRepo, _fixture.OrderRepo, _fixture.ComplaintRepo),
            escalationPolicy);
    }

    [Fact]
    public void ProcessPendingEscalations_FeatureDisabled_NoOp()
    {
        _opts.EscalationEnabled = false;
        var svc = BuildService();
        var trace = new ReasoningTrace
        {
            SessionId = "s1",
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = WellKnown.AgentNames.Order,
                    PostToolReflection = new PostToolReflection
                    {
                        Status = WellKnown.TaskStatuses.NeedsEscalation,
                        HandoffReason = "yetersiz veri"
                    }
                }
            }
        };

        svc.ProcessPendingEscalations(trace, "soru", "yanıt");
        _sink.GetOpen().Should().BeEmpty();
    }

    [Fact]
    public void ProcessPendingEscalations_NoCandidates_NoOp()
    {
        var svc = BuildService();
        var trace = new ReasoningTrace
        {
            SessionId = "s1",
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = WellKnown.AgentNames.Order,
                    PostToolReflection = new PostToolReflection { Status = WellKnown.TaskStatuses.Completed }
                }
            }
        };
        svc.ProcessPendingEscalations(trace, "soru", "yanıt");
        _sink.GetOpen().Should().BeEmpty();
    }

    [Fact]
    public void ProcessPendingEscalations_NeedsEscalation_CreatesEscalation()
    {
        var svc = BuildService();
        var trace = new ReasoningTrace
        {
            SessionId = "s1",
            TraceId = "t1",
            SpecialistReasonings = new List<SpecialistReasoning>
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

        svc.ProcessPendingEscalations(trace, "soru", "yanıt");
        var open = _sink.GetOpen();
        open.Should().ContainSingle();
        open[0].Reason.Should().Be("manuel inceleme");
        open[0].SessionId.Should().Be("s1");
    }

    [Fact]
    public void ProcessPendingEscalations_DuplicateSession_SecondSkipped()
    {
        var svc = BuildService();
        var trace = new ReasoningTrace
        {
            SessionId = "s1",
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = WellKnown.AgentNames.Complaint,
                    PostToolReflection = new PostToolReflection
                    {
                        Status = WellKnown.TaskStatuses.NeedsEscalation,
                        HandoffReason = "ilk"
                    }
                }
            }
        };

        svc.ProcessPendingEscalations(trace, "q", "r");
        // İkinci çağrı session zaten açık olduğu için skip etmeli
        var trace2 = new ReasoningTrace
        {
            SessionId = "s1",
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = WellKnown.AgentNames.Order,
                    PostToolReflection = new PostToolReflection
                    {
                        Status = WellKnown.TaskStatuses.NeedsEscalation,
                        HandoffReason = "ikinci"
                    }
                }
            }
        };
        svc.ProcessPendingEscalations(trace2, "q", "r");
        _sink.GetOpen().Should().ContainSingle();
    }

    [Fact]
    public void ProcessPendingEscalations_TruncatesLongResponse()
    {
        var svc = BuildService();
        var trace = new ReasoningTrace
        {
            SessionId = "s1",
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = WellKnown.AgentNames.Complaint,
                    PostToolReflection = new PostToolReflection
                    {
                        Status = WellKnown.TaskStatuses.NeedsEscalation,
                        HandoffReason = "neden"
                    }
                }
            }
        };

        var longResponse = new string('x', 1000);
        svc.ProcessPendingEscalations(trace, "q", longResponse);
        var esc = _sink.GetOpen()[0];
        esc.ResponseSummary!.Length.Should().BeLessThanOrEqualTo(501);
        esc.ResponseSummary.Should().EndWith("…");
    }

    [Fact]
    public void ProcessPendingEscalations_TraceLevelDedup_LastReasoningWins()
    {
        var svc = BuildService();
        var trace = new ReasoningTrace
        {
            SessionId = "s1",
            SpecialistReasonings = new List<SpecialistReasoning>
            {
                new()
                {
                    AgentName = WellKnown.AgentNames.Complaint,
                    PostToolReflection = new PostToolReflection
                    {
                        Status = WellKnown.TaskStatuses.NeedsEscalation,
                        HandoffReason = "ilk_neden"
                    }
                },
                new()
                {
                    AgentName = WellKnown.AgentNames.Complaint,
                    PostToolReflection = new PostToolReflection
                    {
                        Status = WellKnown.TaskStatuses.NeedsEscalation,
                        HandoffReason = "son_neden"
                    }
                }
            }
        };

        svc.ProcessPendingEscalations(trace, "q", "r");
        var open = _sink.GetOpen();
        open.Should().ContainSingle();
        open[0].Reason.Should().Be("son_neden");
    }
}
