// Tests/Agents/TurnFinalizerPureLogicTests.cs
//
// Bulgu 4.5: TurnFinalizer.PopulateAgentVisitOutputs eskiden AgentVisit.AgentName'i
// `Split('_', 2)[0]` ile "temel ad"a indirgeyip bunu sabit "Planning"/"Response" string
// literalleriyle ve specialist eşleşmesinde TERS yönde (s.AgentName.StartsWith(baseName))
// karşılaştırıyordu. Bu, gelecekte bir ajan adı '_' içerirse kırılırdı. Codebase'in kurulu
// deseni (bkz. WorkflowResponseExtractor.ExtractSpecialistReasonings) TERSİNE çalışır: ham
// (muhtemelen suffix'li) değerin TEMİZ WellKnown.AgentNames sabitiyle mi BAŞLADIĞINA bakılır.
// PopulateAgentVisitOutputs bu desene hizalandı; internal yapılıp doğrudan test ediliyor
// (reflection yok — bkz. WorkflowRunnerPureLogicTests deseni).

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class TurnFinalizerPureLogicTests
{
    private static ReasoningTrace TraceWithVisit(string agentName, PlanningResult? planning = null) => new()
    {
        Planning = planning,
        AgentVisits = { new AgentVisit { AgentName = agentName } }
    };

    [Fact]
    public void PopulateAgentVisitOutputs_PlanningAgent_UsesPlanningResult()
    {
        var planning = new PlanningResult { SelectedAgent = "OrderAgent" };
        var trace = TraceWithVisit(WellKnown.AgentNames.Planning, planning);

        TurnFinalizer.PopulateAgentVisitOutputs(trace, finalResult: "son yanit");

        trace.AgentVisits[0].Output.Should().Contain("OrderAgent");
    }

    [Fact]
    public void PopulateAgentVisitOutputs_ResponseAgent_UsesFinalResult()
    {
        var trace = TraceWithVisit(WellKnown.AgentNames.Response);

        TurnFinalizer.PopulateAgentVisitOutputs(trace, finalResult: "musteriye giden son metin");

        trace.AgentVisits[0].Output.Should().Be("musteriye giden son metin");
    }

    [Fact]
    public void PopulateAgentVisitOutputs_SpecialistAgent_MatchesBySpecialistReasoningAgentName()
    {
        var trace = TraceWithVisit(WellKnown.AgentNames.Order);
        trace.SpecialistReasonings.Add(new SpecialistReasoning { AgentName = WellKnown.AgentNames.Order });

        TurnFinalizer.PopulateAgentVisitOutputs(trace, finalResult: "");

        trace.AgentVisits[0].Output.Should().NotBeNullOrEmpty();
        trace.AgentVisits[0].Output.Should().Contain(WellKnown.AgentNames.Order);
    }

    [Fact]
    public void PopulateAgentVisitOutputs_AgentNameWithSuffix_StillMatchesCleanKnownName()
    {
        // MAF'ın executor id'ye bir suffix eklediği (ör. "OrderAgent_1") varsayımsal durum —
        // eski Split('_') mantığı bu durumda ZATEN doğru çalışıyordu; bu test regresyona
        // karşı korur (yön değişince suffix toleransı kaybolmamalı).
        var trace = TraceWithVisit("OrderAgent_1");
        trace.SpecialistReasonings.Add(new SpecialistReasoning { AgentName = WellKnown.AgentNames.Order });

        TurnFinalizer.PopulateAgentVisitOutputs(trace, finalResult: "");

        trace.AgentVisits[0].Output.Should().NotBeNullOrEmpty(
            "suffix'li executor id, temiz WellKnown adıyla başladığı için hâlâ eşleşmeli");
    }

    [Fact]
    public void PopulateAgentVisitOutputs_AgentNameContainingUnderscore_DoesNotBreakPlanningMatch()
    {
        // Bulgu 4.5'in tam senaryosu: ajan adının kendisi '_' içerse bile (varsayımsal,
        // bugün gerçek ad kümesinde yok ama gelecekte olabilir) eşleşme kırılmamalı —
        // artık Split('_', 2)[0] YOK, doğrudan tam ad üzerinden StartsWith yapılıyor.
        var planning = new PlanningResult { SelectedAgent = "X" };
        var trace = TraceWithVisit(WellKnown.AgentNames.Planning + "_extra_suffix", planning);

        TurnFinalizer.PopulateAgentVisitOutputs(trace, finalResult: "");

        trace.AgentVisits[0].Output.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PopulateAgentVisitOutputs_AlreadyHasOutput_DoesNotOverwrite()
    {
        var trace = TraceWithVisit(WellKnown.AgentNames.Response);
        trace.AgentVisits[0].Output = "zaten dolu";

        TurnFinalizer.PopulateAgentVisitOutputs(trace, finalResult: "yeni metin");

        trace.AgentVisits[0].Output.Should().Be("zaten dolu");
    }

    [Fact]
    public void PopulateAgentVisitOutputs_UnknownAgentNoReasonings_LeavesOutputNull()
    {
        var trace = TraceWithVisit("BilinmeyenAjan");

        TurnFinalizer.PopulateAgentVisitOutputs(trace, finalResult: "");

        trace.AgentVisits[0].Output.Should().BeNullOrEmpty();
    }
}
