// Tests/Agents/WorkflowRunnerPureLogicTests.cs
// WorkflowRunner'ın SAF (bağımlılıksız) mantığı: ResponseStreamFilter ve iki "kod garantisi"
// metodu (EnsureHumanHandoffEscalation / EnsureSideEffectToolCompletion).
//
// Neden CustomerSupportTeamTests'ten ayrıldı: o sınıf [Collection("PostgresCatalog")] ile
// Testcontainers/Docker'a bağlı — WorkflowRunner örneği kurmak için gerçek tool servisi
// gerekiyor. Buradaki testlerin hiçbiri WorkflowRunner ÖRNEĞİNE ihtiyaç duymuyor (hepsi
// static metot veya bağımsız nested sınıf), dolayısıyla Docker'sız bir ortamda da koşarlar.
// Docker kapalıyken kırmızıya dönen test yüzeyini bu ayrım kadar küçültür.
//
// Reflection KULLANILMAZ: test edilen üyeler internal, InternalsVisibleTo ile doğrudan
// çağrılıyor — yeniden adlandırma çalışma zamanında değil derleme zamanında yakalanır.

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Tests.Agents;

public class WorkflowRunnerPureLogicTests
{
    // ResponseStreamFilter — ResponseAgent gerçek token akışından "TERMINATE: reason=..."
    // işaretini (ve ardından gelen self-critique JSON'unu) kullanıcıya sızdırmamalı.
    // WorkflowRunner içindeki internal nested class; InternalsVisibleTo sayesinde doğrudan
    // örneklenip çağrılıyor (reflection yok → yeniden adlandırma derleme zamanında yakalanır).

    private static WorkflowRunner.ResponseStreamFilter NewFilter() => new();

    private static string Feed(WorkflowRunner.ResponseStreamFilter filter, string chunk)
        => filter.Feed(chunk);

    [Fact]
    public void ResponseStreamFilter_PlainTextNoMarker_PassesThroughEventually()
    {
        var filter = NewFilter();
        var emitted = Feed(filter, "Merhaba, ") + Feed(filter, "siparişiniz kargoya verildi.");
        emitted.Should().Contain("Merhaba");
    }

    [Fact]
    public void ResponseStreamFilter_MarkerInSingleChunk_CutsOffAtMarker()
    {
        var filter = NewFilter();
        var emitted = Feed(filter, "Cevabınız burada. TERMINATE: reason=completed");
        emitted.Should().Be("Cevabınız burada. ");
        emitted.Should().NotContain("TERMINATE");
    }

    [Fact]
    public void ResponseStreamFilter_MarkerSplitAcrossChunks_NeverLeaksPartialMarker()
    {
        var filter = NewFilter();
        var emitted = Feed(filter, "Tamamdır. TERM");
        emitted += Feed(filter, "INATE: reason=completed");

        emitted.Should().Be("Tamamdır. ");
        emitted.Should().NotContain("TERM");
    }

    [Fact]
    public void ResponseStreamFilter_AfterCutoff_SuppressesFurtherChunks()
    {
        var filter = NewFilter();
        Feed(filter, "Cevap. TERMINATE: reason=completed");
        var afterCutoff = Feed(filter, "\n```json\n{\"selfCritique\":\"...\"}\n```");

        afterCutoff.Should().BeEmpty();
    }

    // EnsureHumanHandoffEscalation — human_handoff_tool çağrıldığında eskalasyonun LLM'in
    // postToolReflection'ı doğru üretmesine bakılmaksızın garanti altına alındığını test eder
    // (Bulgu C). internal static; InternalsVisibleTo ile doğrudan çağrılıyor.

    private static void InvokeEnsureHumanHandoffEscalation(
        IEnumerable<ChatMessage> messages, List<SpecialistReasoning> reasonings)
        => WorkflowRunner.EnsureHumanHandoffEscalation(messages, reasonings);

    private static ChatMessage HumanHandoffToolCallMessage() =>
        new(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call1", WellKnown.ToolNames.HumanHandoff,
                new Dictionary<string, object?> { ["reason"] = "bottan sıkıldım" })
        });

    [Fact]
    public void EnsureHumanHandoffEscalation_ToolCalledButReflectionMissing_SynthesizesEscalation()
    {
        // LLM tool'u çağırdı ama reflection JSON'unu hiç üretmedi/parse edilemedi —
        // en kırılgan senaryo.
        var reasonings = new List<SpecialistReasoning>();

        InvokeEnsureHumanHandoffEscalation([HumanHandoffToolCallMessage()], reasonings);

        reasonings.Should().ContainSingle();
        reasonings[0].AgentName.Should().Be(WellKnown.AgentNames.HumanHandoff);
        reasonings[0].PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.NeedsEscalation);
    }

    [Fact]
    public void EnsureHumanHandoffEscalation_ToolCalledButWrongStatus_OverridesToNeedsEscalation()
    {
        // LLM tool'u çağırdı ama status'ü yanlışlıkla "done" bıraktı.
        var reasonings = new List<SpecialistReasoning>
        {
            new()
            {
                AgentName = WellKnown.AgentNames.HumanHandoff,
                PostToolReflection = new PostToolReflection
                {
                    Status = WellKnown.TaskStatuses.Done,
                    Summary = "orijinal özet"
                }
            }
        };

        InvokeEnsureHumanHandoffEscalation([HumanHandoffToolCallMessage()], reasonings);

        reasonings.Should().ContainSingle();
        reasonings[0].PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.NeedsEscalation);
        reasonings[0].PostToolReflection!.Summary.Should().Be("orijinal özet"); // mevcut özet korunur
    }

    [Fact]
    public void EnsureHumanHandoffEscalation_ToolCalledAndReflectionAlreadyCorrect_LeavesUnchanged()
    {
        var original = new SpecialistReasoning
        {
            AgentName = WellKnown.AgentNames.HumanHandoff,
            PostToolReflection = new PostToolReflection
            {
                Status = WellKnown.TaskStatuses.NeedsEscalation,
                HandoffReason = "orijinal neden"
            }
        };
        var reasonings = new List<SpecialistReasoning> { original };

        InvokeEnsureHumanHandoffEscalation([HumanHandoffToolCallMessage()], reasonings);

        reasonings.Should().ContainSingle();
        reasonings[0].Should().BeSameAs(original); // dokunulmadı
    }

    [Fact]
    public void EnsureHumanHandoffEscalation_ToolNotCalled_NoOp()
    {
        var reasonings = new List<SpecialistReasoning>();
        var msg = new ChatMessage(ChatRole.Assistant, "sipariş durumu: kargoda");

        InvokeEnsureHumanHandoffEscalation([msg], reasonings);

        reasonings.Should().BeEmpty();
    }

    [Fact]
    public void EnsureHumanHandoffEscalation_StatusOverridden_PreservesSiblingFields()
    {
        // Düzeltme YERİNDE yapılmalı: LLM'in ürettiği diğer alanlar korunmalı.
        // MissingContext ayrıca fonksiyonel — EscalationPolicyService bunu doğrudan
        // EscalationRequest'e kopyalıyor (bkz. EscalationPolicyService.cs).
        var original = new SpecialistReasoning
        {
            AgentName = WellKnown.AgentNames.HumanHandoff,
            ResultConfidence = 0.91,
            ResultNotes = "tool başarıyla çalıştı",
            PreToolCheck = new PreToolCheck { CanProceed = true, Reasoning = "sebep mevcut" },
            PostToolReflection = new PostToolReflection
            {
                Status = WellKnown.TaskStatuses.Done,   // ← yanlış status
                TaskComplete = true,
                Summary = "orijinal özet",
                HandoffReason = "orijinal neden",
                MissingContext = ["müşteri numarası", "sipariş geçmişi"]
            }
        };
        var reasonings = new List<SpecialistReasoning> { original };

        InvokeEnsureHumanHandoffEscalation([HumanHandoffToolCallMessage()], reasonings);

        reasonings.Should().ContainSingle();
        var r = reasonings[0];

        // Düzeltilenler
        r.PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.NeedsEscalation);
        r.PostToolReflection.TaskComplete.Should().BeFalse();

        // Korunanlar
        r.ResultConfidence.Should().Be(0.91);
        r.ResultNotes.Should().Be("tool başarıyla çalıştı");
        r.PreToolCheck!.Reasoning.Should().Be("sebep mevcut");
        r.PostToolReflection.Summary.Should().Be("orijinal özet");
        r.PostToolReflection.HandoffReason.Should().Be("orijinal neden");
        r.PostToolReflection.MissingContext.Should().BeEquivalentTo(["müşteri numarası", "sipariş geçmişi"]);
    }

    // EnsureSideEffectToolCompletion — order_placement_tool/order_cancel_tool/return_request_tool
    // (OrderAgent) ve complaint_registration_tool (ComplaintAgent) BAŞARIYLA çağrıldığında görevin
    // tamamlandığının (status=done) LLM'in reflection'ından bağımsız garanti altına alındığını
    // test eder (Bulgu #5 — EnsureHumanHandoffEscalation ile aynı desen, ters yönde; başlangıçta
    // yalnızca order_cancel_tool'a eklenmişti, sonra tüm HITL onaylı yan-etkili tool'lara
    // genelleştirildi). internal static; InternalsVisibleTo ile doğrudan çağrılıyor.

    private static void InvokeEnsureSideEffectToolCompletion(
        IEnumerable<ChatMessage> messages, List<SpecialistReasoning> reasonings, string agentName, IReadOnlySet<string> toolNames)
        => WorkflowRunner.EnsureSideEffectToolCompletion(messages, reasonings, agentName, toolNames);

    private static List<ChatMessage> ToolCallAndResultMessages(string toolName, bool success) =>
    [
        new(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call1", toolName,
                new Dictionary<string, object?> { ["orderId"] = "1030", ["reason"] = "vazgeçtim" })
        }),
        new(ChatRole.Tool, new List<AIContent>
        {
            new FunctionResultContent("call1", new ToolResult { Success = success, Message = "sonuç" })
        })
    ];

    public static IEnumerable<object[]> OrderAgentSideEffectToolCases =>
    [
        [WellKnown.ToolNames.OrderPlacement],
        [WellKnown.ToolNames.OrderCancel],
        [WellKnown.ToolNames.ReturnRequest]
    ];

    // Üretimdeki setlerle AYNI kaynaktan türetilir (WorkflowRunner de bunu kullanıyor) —
    // burada elle ikinci bir kopya tutmak, bu testlerin gerçekte kullanılmayan bir set
    // üzerinde yeşil kalmasına yol açardı. Setlerin İÇERİĞİ ayrıca WellKnownTests'te kilitli.
    private static readonly IReadOnlySet<string> OrderAgentSideEffectTools =
        WellKnown.SideEffectToolsOf(WellKnown.AgentNames.Order);

    private static readonly IReadOnlySet<string> ComplaintAgentSideEffectTools =
        WellKnown.SideEffectToolsOf(WellKnown.AgentNames.Complaint);

    [Theory]
    [MemberData(nameof(OrderAgentSideEffectToolCases))]
    public void EnsureSideEffectToolCompletion_OrderAgentTool_SucceededButReflectionMissing_SynthesizesDone(string toolName)
    {
        var reasonings = new List<SpecialistReasoning>();

        InvokeEnsureSideEffectToolCompletion(
            ToolCallAndResultMessages(toolName, success: true), reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

        reasonings.Should().ContainSingle();
        reasonings[0].AgentName.Should().Be(WellKnown.AgentNames.Order);
        reasonings[0].PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.Done);
        reasonings[0].PostToolReflection!.TaskComplete.Should().BeTrue();
    }

    [Fact]
    public void EnsureSideEffectToolCompletion_ComplaintRegistration_SucceededButReflectionMissing_SynthesizesDone()
    {
        var reasonings = new List<SpecialistReasoning>();

        InvokeEnsureSideEffectToolCompletion(
            ToolCallAndResultMessages(WellKnown.ToolNames.ComplaintRegistration, success: true),
            reasonings, WellKnown.AgentNames.Complaint, ComplaintAgentSideEffectTools);

        reasonings.Should().ContainSingle();
        reasonings[0].AgentName.Should().Be(WellKnown.AgentNames.Complaint);
        reasonings[0].PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.Done);
        reasonings[0].PostToolReflection!.TaskComplete.Should().BeTrue();
    }

    [Fact]
    public void EnsureSideEffectToolCompletion_ToolSucceededButWrongStatus_OverridesToDone()
    {
        var reasonings = new List<SpecialistReasoning>
        {
            new()
            {
                AgentName = WellKnown.AgentNames.Order,
                PostToolReflection = new PostToolReflection
                {
                    Status = WellKnown.TaskStatuses.NeedsFollowUp,
                    Summary = "orijinal özet"
                }
            }
        };

        InvokeEnsureSideEffectToolCompletion(
            ToolCallAndResultMessages(WellKnown.ToolNames.OrderCancel, success: true),
            reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

        reasonings.Should().ContainSingle();
        reasonings[0].PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.Done);
        reasonings[0].PostToolReflection!.Summary.Should().Be("orijinal özet"); // mevcut özet korunur
    }

    [Fact]
    public void EnsureSideEffectToolCompletion_ToolSucceededAndReflectionAlreadyCorrect_LeavesUnchanged()
    {
        var original = new SpecialistReasoning
        {
            AgentName = WellKnown.AgentNames.Order,
            PostToolReflection = new PostToolReflection { Status = WellKnown.TaskStatuses.Done, TaskComplete = true }
        };
        var reasonings = new List<SpecialistReasoning> { original };

        InvokeEnsureSideEffectToolCompletion(
            ToolCallAndResultMessages(WellKnown.ToolNames.OrderCancel, success: true),
            reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

        reasonings.Should().ContainSingle();
        reasonings[0].Should().BeSameAs(original); // dokunulmadı
    }

    [Fact]
    public void EnsureSideEffectToolCompletion_ToolFailed_NoOp()
    {
        // Bilinçli asimetri: başarısızlık yönünde ASLA zorlanmaz — bkz. metodun XML doc'u.
        var reasonings = new List<SpecialistReasoning>
        {
            new()
            {
                AgentName = WellKnown.AgentNames.Order,
                PostToolReflection = new PostToolReflection { Status = WellKnown.TaskStatuses.NeedsFollowUp }
            }
        };

        InvokeEnsureSideEffectToolCompletion(
            ToolCallAndResultMessages(WellKnown.ToolNames.OrderCancel, success: false),
            reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

        reasonings[0].PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.NeedsFollowUp);
    }

    [Fact]
    public void EnsureSideEffectToolCompletion_ToolNotCalled_NoOp()
    {
        var reasonings = new List<SpecialistReasoning>();
        var msg = new ChatMessage(ChatRole.Assistant, "sipariş durumu: kargoda");

        InvokeEnsureSideEffectToolCompletion([msg], reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

        reasonings.Should().BeEmpty();
    }

    [Fact]
    public void EnsureSideEffectToolCompletion_ComplaintToolCalled_DoesNotAffectOrderAgentSet()
    {
        // complaint_registration_tool, OrderAgentSideEffectTools içinde YOK — OrderAgent
        // çağrısı için invoke edilirse no-op olmalı (yanlış tool setiyle eşleşmemeli).
        var reasonings = new List<SpecialistReasoning>();

        InvokeEnsureSideEffectToolCompletion(
            ToolCallAndResultMessages(WellKnown.ToolNames.ComplaintRegistration, success: true),
            reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

        reasonings.Should().BeEmpty();
    }

    [Fact]
    public void EnsureSideEffectToolCompletion_ResultIsJsonElement_StillDetectsSuccess()
    {
        // AIFunctionFactory sonucu serileştirme yoluna bağlı olarak JsonElement de olabilir.
        var json = System.Text.Json.JsonDocument.Parse("""{"success":true,"message":"ok"}""").RootElement;
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call1", WellKnown.ToolNames.OrderCancel,
                    new Dictionary<string, object?> { ["orderId"] = "1030" })
            }),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("call1", json) })
        };
        var reasonings = new List<SpecialistReasoning>();

        InvokeEnsureSideEffectToolCompletion(messages, reasonings, WellKnown.AgentNames.Order, OrderAgentSideEffectTools);

        reasonings.Should().ContainSingle();
        reasonings[0].PostToolReflection!.StatusEnum.Should().Be(TaskCompletionStatus.Done);
    }
}
