// Deterministik yönlendirici: Reasoning'in niyetinden ilk ajanı tablodan seçer (Planning çağrısı yerine). Emin olmadığı her durumda
// SUSAR (Abstain) — o zaman eski Planning yolu çalışır. Karar mantığı saf: LLM yok.

using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class IntentRouterTests
{
    private static ReasoningResult R(string intent, params string[] requiredInfo) => new()
    {
        Intent = intent, ConfidenceScore = 0.9, RequiredInfo = requiredInfo.ToList()
    };

    [Theory]
    [InlineData(WellKnown.Intents.OrderCreation, WellKnown.AgentNames.Order)]
    [InlineData(WellKnown.Intents.OrderInquiry, WellKnown.AgentNames.Order)]
    [InlineData(WellKnown.Intents.OrderListing, WellKnown.AgentNames.Order)]
    [InlineData(WellKnown.Intents.OrderCancellation, WellKnown.AgentNames.Order)]
    [InlineData(WellKnown.Intents.ReturnRequest, WellKnown.AgentNames.Order)]
    [InlineData(WellKnown.Intents.Complaint, WellKnown.AgentNames.Complaint)]
    [InlineData(WellKnown.Intents.ProductInfo, WellKnown.AgentNames.Product)]
    [InlineData(WellKnown.Intents.HumanHandoffRequest, WellKnown.AgentNames.HumanHandoff)]
    [InlineData(WellKnown.Intents.General, WellKnown.AgentNames.Response)]
    public void EachKnownIntent_GoesToItsAgent(string intent, string agent)
    {
        var d = IntentRouter.Decide(R(intent));

        d.Decided.Should().BeTrue();
        d.Agent.Should().Be(agent);
    }

    [Theory]
    [InlineData(WellKnown.Intents.OrderCancellation, "sipariş_numarası", "iptal_sebebi")]
    [InlineData(WellKnown.Intents.ReturnRequest, "iade_sebebi")]
    [InlineData(WellKnown.Intents.Complaint, "sipariş_numarası", "şikayet_açıklaması")]
    [InlineData(WellKnown.Intents.OrderCreation, "ürün_adı", "adet")]
    public void MissingInformation_GoesToTheResponseAgent_WhichAsksForItAllAtOnce(string intent, params string[] missing)
    {
        var d = IntentRouter.Decide(R(intent, missing));

        d.Agent.Should().Be(WellKnown.AgentNames.Response, "Planning de needsClarification=true ile aynı yere yönlendiriyordu");
        d.Reason.Should().Contain("required_info");
    }

    [Fact]
    public void ACustomerIdentityEntry_IsNotMissingInformation()
    {
        // Müşteri kimliği girişten bilinir; model yanlışlıkla requiredInfo'ya yazsa bile netleştirme sebebi olmaz.
        IntentRouter.Decide(R(WellKnown.Intents.OrderInquiry, "müşteri_kimliği", "customer_id")).Agent
            .Should().Be(WellKnown.AgentNames.Order);
    }

    [Fact]
    public void AHandoffRequest_IsNeverHeldBackByMissingInformation() =>
        IntentRouter.Decide(R(WellKnown.Intents.HumanHandoffRequest, "sebep")).Agent.Should().Be(WellKnown.AgentNames.HumanHandoff);

    [Theory]
    [InlineData("")]
    [InlineData("bilinmiyor")]
    [InlineData("uydurma_niyet")]
    [InlineData("Sipariş_Sorgulama")]       // büyük/küçük harf: sözleşme tam eşleşme ister
    public void AnUnrecognisedIntent_Abstains(string intent)
    {
        var d = IntentRouter.Decide(R(intent));

        d.Decided.Should().BeFalse();
        d.Agent.Should().BeNull();
        d.Reason.Should().Be("unknown_intent");
    }

    [Fact]
    public void WithoutReasoning_Abstains() => IntentRouter.Decide(null).Reason.Should().Be("no_reasoning");

    [Fact]
    public void AFallbackReasoning_Abstains_BecauseItIsNotARealDecision()
    {
        var r = R(WellKnown.Intents.OrderInquiry);
        r.IsFallback = true;

        var d = IntentRouter.Decide(r);

        d.Decided.Should().BeFalse();
        d.Reason.Should().Be("fallback");
    }

    [Fact]
    public void ASubTaskRun_GoesStraightToItsConstrainedSpecialist()
    {
        var r = R(WellKnown.Intents.OrderInquiry);
        r.ConstrainedTargetAgent = WellKnown.AgentNames.Product;

        var d = IntentRouter.Decide(r);

        d.Agent.Should().Be(WellKnown.AgentNames.Product);
        d.Reason.Should().Be("subtask");
    }

    [Fact]
    public void ASubTaskRun_IgnoresMissingInformation_BecauseTheSplitAlreadyChoseTheAgent()
    {
        var r = R(WellKnown.Intents.Complaint, "şikayet_açıklaması");
        r.ConstrainedTargetAgent = WellKnown.AgentNames.Complaint;

        IntentRouter.Decide(r).Agent.Should().Be(WellKnown.AgentNames.Complaint);
    }

    [Fact]
    public void EveryAgentTheRouterCanReturn_IsARealWorkflowParticipant()
    {
        var all = new[]
        {
            WellKnown.Intents.OrderCreation, WellKnown.Intents.OrderInquiry, WellKnown.Intents.OrderListing, WellKnown.Intents.OrderCancellation,
            WellKnown.Intents.ReturnRequest, WellKnown.Intents.Complaint, WellKnown.Intents.ProductInfo, WellKnown.Intents.HumanHandoffRequest,
            WellKnown.Intents.General
        };

        foreach (var intent in all)
            WellKnown.AgentNames.All.Should().Contain(IntentRouter.Decide(R(intent)).Agent!);
    }

    [Fact]
    public void EveryLlmProducedIntent_IsEitherRoutedOrDeliberatelyAbstained()
    {
        // Yeni bir niyet eklenip tabloya yazılmazsa sessizce Planning'e düşer — bu test o unutkanlığı görünür kılar.
        foreach (var intent in WellKnown.Intents.LlmProduced)
            IntentRouter.Decide(R(intent)).Decided.Should().BeTrue($"'{intent}' için yönlendirme kuralı tanımlı olmalı");
    }
}

public class RoutingComparisonTests
{
    private static PlanningResult Plan(string agent, bool clarify = false) => new() { SelectedAgent = agent, NeedsClarification = clarify };

    [Fact]
    public void TheSameAgent_IsAMatch()
    {
        var c = RoutingComparison.Compare(new RouteDecision(WellKnown.AgentNames.Order, "intent"), Plan(WellKnown.AgentNames.Order));

        c.Outcome.Should().Be(RoutingComparison.Match);
        c.PlannerAgent.Should().Be(WellKnown.AgentNames.Order);
    }

    [Fact]
    public void ADifferentAgent_IsAMismatch()
    {
        var c = RoutingComparison.Compare(new RouteDecision(WellKnown.AgentNames.Order, "intent"), Plan(WellKnown.AgentNames.Complaint));

        c.Outcome.Should().Be(RoutingComparison.Mismatch);
        c.PlannerAgent.Should().Be(WellKnown.AgentNames.Complaint);
    }

    [Fact]
    public void AClarifyingPlan_CountsAsTheResponseAgent()
    {
        // Planning needsClarification=true iken selectedAgent ne olursa olsun yönlendirme ResponseAgent'a gider.
        var match = RoutingComparison.Compare(new RouteDecision(WellKnown.AgentNames.Response, "required_info"), Plan(WellKnown.AgentNames.Order, clarify: true));
        var miss = RoutingComparison.Compare(new RouteDecision(WellKnown.AgentNames.Order, "intent"), Plan(WellKnown.AgentNames.Order, clarify: true));

        match.Outcome.Should().Be(RoutingComparison.Match);
        miss.Outcome.Should().Be(RoutingComparison.Mismatch);
        miss.PlannerAgent.Should().Be(WellKnown.AgentNames.Response);
    }

    [Fact]
    public void AnAbstainingRouter_IsReportedSeparately_WithThePlannersChoice()
    {
        var c = RoutingComparison.Compare(RouteDecision.Abstain("unknown_intent"), Plan(WellKnown.AgentNames.Product));

        c.Outcome.Should().Be(RoutingComparison.RouterAbstained);
        c.PlannerAgent.Should().Be(WellKnown.AgentNames.Product);
    }

    [Fact]
    public void WithoutAPlan_NothingCanBeCompared()
    {
        var c = RoutingComparison.Compare(new RouteDecision(WellKnown.AgentNames.Order, "intent"), null);

        c.Outcome.Should().Be(RoutingComparison.Unobserved);
        c.PlannerAgent.Should().Be("-");
    }

    [Fact]
    public void AnUnknownPlannedAgent_IsLabelledUnknown_NotEchoed()
    {
        // Model uydurma bir ad üretirse metrik etiketi kontrolsüz büyümesin.
        var c = RoutingComparison.Compare(new RouteDecision(WellKnown.AgentNames.Order, "intent"), Plan("uydurma ajan <script>"));

        c.PlannerAgent.Should().Be("unknown");
        c.Outcome.Should().Be(RoutingComparison.Mismatch);
    }
}
