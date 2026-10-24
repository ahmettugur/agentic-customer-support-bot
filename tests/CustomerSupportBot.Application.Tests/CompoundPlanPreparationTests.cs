// Bileşik sorgu planının hazırlanması: modelin gerçekçi kusurları ONARILIR, yalnızca yürütülemez ya da uydurma numara içeren
// planlar REDDEDİLİR. Red artık turu düşürmez — çağıran (DecomposedRunner) bölmeden tek koşuyla yürür (bkz. DecomposedRunnerTests).
//
// Arka plan: canlı probda üç gerçekçi model çıktısı planı düşürüyor ve müşteriye "Alt görev 2 desteklenmeyen entity içeriyor"
// gibi iç bir hata metni gidiyordu: ürün alanı taşıyan alt görev, önceki mesajdan gelen sipariş numarası, açıklamada tekrar
// edilmeyen numara.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Services.Reasoning;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Tests;

public class CompoundPlanPreparationTests
{
    private static readonly ParallelExecutionOptions Options = new() { MaxSubTasks = 6 };

    private static SubTask Sub(int order, string description, string agent, Dictionary<string, string>? entities = null, List<int>? deps = null) =>
        new() { Order = order, Description = description, TargetAgent = agent, Intent = "x", Entities = entities ?? new(), Dependencies = deps ?? [] };

    private static ReasoningResult Plan(params SubTask[] subs) => new()
    {
        SubTasks = subs.ToList(),
        VerifiedEntities = new VerifiedEntities
        {
            CustomerId = new VerifiedEntity { Value = "1027", Source = EntitySource.SessionState, Verification = EntityVerification.Verified }
        }
    };

    // ── Onarılan kusurlar ──────────────────────────────────────────────────────

    [Fact]
    public void UnknownEntityKeys_AreDropped_NotFatal()
    {
        var r = Plan(
            Sub(1, "1030 siparişinin durumunu sorgula", WellKnown.AgentNames.Order, new() { ["order_id"] = "1030" }),
            Sub(2, "2 kahve için stok kontrolü", WellKnown.AgentNames.Product, new() { ["product_name"] = "kahve", ["quantity"] = "2" }));

        var p = SubTaskOrchestrator.PrepareExecutionPlan(r, "1030 siparişim nerede ve 2 kahve sipariş etmek istiyorum", Options);

        p.IsValid.Should().BeTrue();
        p.Plan![1].Entities.Should().BeEmpty();
        p.Plan[0].Entities.Should().Contain("order_id", "1030");
        p.Repairs.Should().Contain(x => x.Contains("product_name"));
    }

    [Fact]
    public void AnIdFromEarlierInTheConversation_IsGrounded()
    {
        var r = Plan(
            Sub(1, "1030 siparişini iptal et", WellKnown.AgentNames.Order, new() { ["order_id"] = "1030" }),
            Sub(2, "ürün listesini göster", WellKnown.AgentNames.Product));
        var history = new List<ConversationMessage>
        {
            new(ConversationRoles.User, "1030 numaralı siparişim nerede?"),
            new(ConversationRoles.Assistant, "1030 numaralı siparişiniz kargoda.")
        };

        SubTaskOrchestrator.PrepareExecutionPlan(r, "o siparişi iptal et ve ürün listesini göster", Options, history)
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void AnIdTheBotMentionedEarlier_IsGroundedToo()
    {
        // "Son siparişiniz 1057" → "onu iptal et": numara yalnızca botun önceki mesajında (araç çıktısından) geçiyor.
        var r = Plan(
            Sub(1, "1057 siparişini iptal et", WellKnown.AgentNames.Order, new() { ["order_id"] = "1057" }),
            Sub(2, "ürünleri listele", WellKnown.AgentNames.Product));
        var history = new List<ConversationMessage> { new(ConversationRoles.Assistant, "Son siparişiniz 1057, kargoda.") };

        SubTaskOrchestrator.PrepareExecutionPlan(r, "onu iptal et ve ürünleri listele", Options, history).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ADescriptionThatDoesNotRepeatTheId_IsAccepted_WhenTheIdIsInTheMessage()
    {
        var r = Plan(
            Sub(1, "Sipariş durumunu sorgula", WellKnown.AgentNames.Order, new() { ["order_id"] = "1030" }),
            Sub(2, "Şikayet kaydı aç", WellKnown.AgentNames.Complaint, new() { ["order_id"] = "1042" }));

        SubTaskOrchestrator.PrepareExecutionPlan(r, "1030 siparişim nerede ve 1042 için şikayet açmak istiyorum", Options)
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void ANonNumericIdValue_IsDropped()
    {
        var r = Plan(
            Sub(1, "son siparişi sorgula", WellKnown.AgentNames.Order, new() { ["order_id"] = "son sipariş" }),
            Sub(2, "ürünleri listele", WellKnown.AgentNames.Product));

        var p = SubTaskOrchestrator.PrepareExecutionPlan(r, "son siparişim nerede ve ürünleri listele", Options);

        p.IsValid.Should().BeTrue();
        p.Plan![0].Entities.Should().BeEmpty();
    }

    [Fact]
    public void NumbersInTheDescription_ThatComeFromTheMessage_AreFine()
    {
        var r = Plan(
            Sub(1, "2026 model telefonları listele", WellKnown.AgentNames.Product),
            Sub(2, "1500 TL üstü siparişlerimi sorgula", WellKnown.AgentNames.Order));

        SubTaskOrchestrator.PrepareExecutionPlan(r, "2026 model telefonları göster ve 1500 TL üstü siparişlerim ne durumda", Options)
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void TheOriginalReasoning_IsNotMutated()
    {
        var r = Plan(
            Sub(1, "1030 durumunu sorgula", WellKnown.AgentNames.Order, new() { ["order_id"] = "1030" }),
            Sub(2, "kahve stoku", WellKnown.AgentNames.Product, new() { ["product_name"] = "kahve" }));

        SubTaskOrchestrator.PrepareExecutionPlan(r, "1030 nerede ve kahve var mı", Options);

        r.SubTasks[1].Entities.Should().ContainKey("product_name", "iz (trace) modelin ürettiği planı olduğu gibi göstermeli");
    }

    // ── Reddedilenler (çağıran bölmeden tek koşuya düşer) ─────────────────────

    [Fact]
    public void AFabricatedIdInTheEntities_IsRejected()
    {
        var r = Plan(
            Sub(1, "1042 siparişini sorgula", WellKnown.AgentNames.Order, new() { ["order_id"] = "1042" }),
            Sub(2, "ürünleri listele", WellKnown.AgentNames.Product));

        var p = SubTaskOrchestrator.PrepareExecutionPlan(r, "yalnızca ürünleri göster", Options);

        p.IsValid.Should().BeFalse();
        p.RejectionCode.Should().Be(PlanRejection.UngroundedEntity);
    }

    [Fact]
    public void AFabricatedIdOnlyInTheDescription_IsRejected()
    {
        var r = Plan(
            Sub(1, "9999 siparişini iptal et", WellKnown.AgentNames.Order),
            Sub(2, "ürünleri listele", WellKnown.AgentNames.Product));

        var p = SubTaskOrchestrator.PrepareExecutionPlan(r, "siparişimi iptal et ve ürünleri listele", Options);

        p.IsValid.Should().BeFalse();
        p.RejectionCode.Should().Be(PlanRejection.UngroundedEntity);
    }

    [Fact]
    public void CrossWiredIds_AreRejected()
    {
        // Alt görev 1'in numarası (1042) yalnızca alt görev 2'nin açıklamasında geçiyor → numaralar yer değiştirmiş.
        var r = Plan(
            Sub(1, "1030 durumunu sorgula", WellKnown.AgentNames.Order, new() { ["order_id"] = "1042" }),
            Sub(2, "1042 için şikayet aç", WellKnown.AgentNames.Complaint, new() { ["order_id"] = "1030" }));

        var p = SubTaskOrchestrator.PrepareExecutionPlan(r, "1030 nerede ve 1042 için şikayet aç", Options);

        p.IsValid.Should().BeFalse();
        p.RejectionCode.Should().Be(PlanRejection.CrossWiredEntity);
    }

    [Fact]
    public void AStructurallyBrokenPlan_IsRejected()
    {
        var r = Plan(
            Sub(1, "a", WellKnown.AgentNames.Order),
            Sub(2, "b", "PlanningAgent"));

        var p = SubTaskOrchestrator.PrepareExecutionPlan(r, "a ve b", Options);

        p.IsValid.Should().BeFalse();
        p.RejectionCode.Should().Be(PlanRejection.Structure);
    }

    [Fact]
    public void TheThrowingWrapper_StillFailsClosed_ForCallersThatNeedIt()
    {
        var r = Plan(
            Sub(1, "1042 siparişini sorgula", WellKnown.AgentNames.Order, new() { ["order_id"] = "1042" }),
            Sub(2, "ürünleri listele", WellKnown.AgentNames.Product));

        var act = () => SubTaskOrchestrator.ValidateExecutionPlan(r, "yalnızca ürünleri göster", Options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*kullanıcı girdisinden doğrulanamadı*");
    }
}
