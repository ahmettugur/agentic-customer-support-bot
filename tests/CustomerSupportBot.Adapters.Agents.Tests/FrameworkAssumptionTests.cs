// Tests/Spikes/FrameworkAssumptionTests.cs
//
// Bu dosya bir DAVRANIŞ testi değil, bir SİGORTA. Trace/routing katmanı MAF'ın belgelenmemiş
// iki iç davranışına bağlı; ikisi de bugün çalışıyor ama framework yükseltmesinde sessizce
// bozulabilir — ne derleme hatası ne çalışma zamanı istisnası verirler, sadece trace boşalır
// veya routing yanlış dala düşer.
//
// Buradaki testler o varsayımları çivileyerek yükseltmede SESSİZLİK yerine KIRMIZI TEST üretir:
//
//   1. TurnToken ayrımı — GroupChatHost seçilmeyen ajanlara da geçmiş senkronu için mesaj
//      yollar (BroadcastAsync) ve bu da ExecutorInvoked/Completed çifti üretir. "Gerçek tur mu
//      broadcast mı" ayrımının TEK sinyali ExecutorInvokedEvent.Data'nın TurnToken olmasıdır.
//      MAF bunu değiştirirse her broadcast bir ajan ziyareti sayılır: IterationCount şişer,
//      UI'da çalışmayan ajanlar "çalışıyor" görünür.
//
//   2. AuthorName öneki — bir mesajın specialist'ten gelip gelmediği msg.AuthorName'in bizim
//      ajan adlarımızdan biriyle BAŞLAMASINA bakılarak belirleniyor. Varsayım: MAF,
//      yapılandırdığımız ChatClientAgent.Name'i AuthorName'e (en azından önek olarak) yansıtır.
//
// Not: MAF'ın "yalnızca seçilen konuşmacı TurnToken alır" davranışının kendisi burada
// doğrulanamaz — canlı bir GroupChat koşusu ve LLM çağrısı gerektirir. Doğrulanan şey, o sinyale
// dayanan KENDİ ayrım mantığımızın doğru çalıştığı ve ilgili framework tiplerinin hâlâ
// beklediğimiz şekle sahip olduğudur (tip/imza değişirse bu dosya derlenmez).

using CustomerSupportBot.Adapters.Agents;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

// MAF'ta da Microsoft.Extensions.AI.RoutingContext var — açık alias olmadan ad çakışıyor.
using RoutingContext = CustomerSupportBot.Adapters.Agents.Routing.RoutingContext;
using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Adapters.Agents.Tests;

public class FrameworkAssumptionTests
{
    /// <summary>
    /// Trace store GERÇEK olanı (<c>InMemoryReasoningTraceStore</c>) kullanılır, sahte değil.
    ///
    /// <para>
    /// Sahte store <c>StartTrace</c> için <c>null</c> döndürüyordu; sözleşme ise non-nullable
    /// <see cref="ReasoningTrace"/> vaat eder. Bu fark, dönen trace'e hiç dokunulmadığı sürece
    /// zararsız görünüyordu — ta ki <c>StartTraceState</c> trace'i okumaya başlayana kadar
    /// (ambient onay bağlamına trace id bağlanması), o an bu dosyadaki üç test birden
    /// NullReferenceException ile düştü. Yani kırılan üretim kodu değil, gerçekçi olmayan
    /// sahteydi. Gerçek store bellek içi ve bağımlılıksız; sahteyi tercih etmenin bir kazancı
    /// yoktu.
    /// </para>
    /// </summary>
    private static WorkflowTraceEventProcessor CreateProcessor() =>
        new(new InMemoryReasoningTraceStore(), Substitute.For<IApprovalContextAccessor>());

    // ═══ 1. TurnToken ayrımı ═══

    /// <summary>
    /// Seçilen konuşmacı: Data == TurnToken → gerçek ajan ziyareti sayılmalı.
    /// </summary>
    [Fact]
    public void ExecutorInvoked_WithTurnToken_CountsAsRealAgentTurn()
    {
        var sut = CreateProcessor();
        var st = sut.StartTraceState(session: null, query: "test", reasoning: null);

        sut.ApplyTraceEvent(st, new ExecutorInvokedEvent(
            WellKnown.AgentNames.Product, new TurnToken(emitEvents: true)));

        st.IterationCount.Should().Be(1);
    }

    /// <summary>
    /// Broadcast hedefi: Data düz mesaj listesi → ziyaret SAYILMAMALI.
    ///
    /// Bu testin kırılması "MAF broadcast'i de TurnToken ile yolluyor" demektir; o durumda
    /// ayrım sinyali geçersizdir ve IterationCount/UI rozetleri güvenilmez hale gelir.
    /// </summary>
    [Fact]
    public void ExecutorInvoked_WithoutTurnToken_IsIgnoredAsBroadcast()
    {
        var sut = CreateProcessor();
        var st = sut.StartTraceState(session: null, query: "test", reasoning: null);

        sut.ApplyTraceEvent(st, new ExecutorInvokedEvent(
            WellKnown.AgentNames.Product,
            new List<ChatMessage> { new(ChatRole.User, "geçmiş senkronu") }));

        st.IterationCount.Should().Be(0);
    }

    /// <summary>
    /// Aynı ajana broadcast + gerçek tur karışık geldiğinde yalnızca gerçek tur sayılmalı —
    /// canlı akışta olan tam olarak budur.
    /// </summary>
    [Fact]
    public void ExecutorInvoked_MixedBroadcastAndRealTurn_CountsOnlyRealTurn()
    {
        var sut = CreateProcessor();
        var st = sut.StartTraceState(session: null, query: "test", reasoning: null);

        sut.ApplyTraceEvent(st, new ExecutorInvokedEvent(
            WellKnown.AgentNames.Order, new List<ChatMessage>()));
        sut.ApplyTraceEvent(st, new ExecutorInvokedEvent(
            WellKnown.AgentNames.Order, new TurnToken(emitEvents: true)));
        sut.ApplyTraceEvent(st, new ExecutorInvokedEvent(
            WellKnown.AgentNames.Complaint, new List<ChatMessage>()));

        st.IterationCount.Should().Be(1);
    }

    /// <summary>
    /// `TurnToken(emitEvents:)` kurucusunun varlığını çivileyen derleme-zamanı kontrolü —
    /// WorkflowRunner her turda bunu gönderiyor.
    /// </summary>
    [Fact]
    public void TurnToken_StillConstructibleWithEmitEvents()
    {
        var token = new TurnToken(emitEvents: true);
        token.Should().NotBeNull();
    }

    // ═══ 2. AuthorName öneki ═══

    [Theory]
    [InlineData(WellKnown.AgentNames.Product)]
    [InlineData(WellKnown.AgentNames.Order)]
    [InlineData(WellKnown.AgentNames.Complaint)]
    [InlineData(WellKnown.AgentNames.HumanHandoff)]
    public void IsSpecialistMessage_ExactAgentName_IsRecognized(string agentName)
    {
        var msg = new ChatMessage(ChatRole.Assistant, "x") { AuthorName = agentName };

        RoutingContext.IsSpecialistMessage(msg).Should().BeTrue();
        RoutingContext.GetSpecialistName(msg).Should().Be(agentName);
    }

    /// <summary>
    /// Eşitlik değil ÖNEK kontrolü yapılmasının sebebi: MAF adın sonuna ek getirebilir.
    /// Bu tolerans bilinçli — testi, toleransın kaybolmadığını doğrular.
    /// </summary>
    [Fact]
    public void IsSpecialistMessage_SuffixedAgentName_IsStillRecognized()
    {
        var msg = new ChatMessage(ChatRole.Assistant, "x")
        {
            AuthorName = WellKnown.AgentNames.Product + "_1"
        };

        RoutingContext.IsSpecialistMessage(msg).Should().BeTrue();
        RoutingContext.GetSpecialistName(msg).Should().Be(WellKnown.AgentNames.Product);
    }

    [Theory]
    [InlineData(WellKnown.AgentNames.Response)]
    [InlineData("PlanningAgent")]
    [InlineData("")]
    public void IsSpecialistMessage_NonSpecialist_IsNotRecognized(string agentName)
    {
        var msg = new ChatMessage(ChatRole.Assistant, "x") { AuthorName = agentName };

        RoutingContext.IsSpecialistMessage(msg).Should().BeFalse();
    }

    [Fact]
    public void IsSpecialistMessage_NullAuthorName_IsNotRecognized()
    {
        var msg = new ChatMessage(ChatRole.Assistant, "x") { AuthorName = null };

        RoutingContext.IsSpecialistMessage(msg).Should().BeFalse();
        RoutingContext.GetSpecialistName(msg).Should().BeNull();
    }

    /// <summary>
    /// Önek eşleşmesinin bilinen kırılganlığını KAYDA GEÇİRİR: mevcut ajan adlarından biriyle
    /// başlayan yeni bir ajan eklenirse (ör. "OrderAgentV2") o ajanın mesajları sessizce
    /// "OrderAgent" sanılır. Bugün böyle bir ad yok; bu test, ileride eklenirse tartışmanın
    /// bilinçli yapılmasını sağlar.
    /// </summary>
    [Fact]
    public void IsSpecialistMessage_PrefixCollision_IsMisclassified_KnownLimitation()
    {
        var msg = new ChatMessage(ChatRole.Assistant, "x")
        {
            AuthorName = WellKnown.AgentNames.Order + "V2"
        };

        // Doğru davranış "false" olurdu; önek eşleşmesi bunu ayırt edemiyor.
        RoutingContext.GetSpecialistName(msg).Should().Be(WellKnown.AgentNames.Order);

        // Çakışma bugün gerçek değil — üretimdeki adlar birbirinin öneki olmamalı.
        var names = WellKnown.AgentNames.Specialists;
        foreach (var a in names)
            foreach (var b in names)
                if (!ReferenceEquals(a, b))
                    a.StartsWith(b, StringComparison.OrdinalIgnoreCase).Should().BeFalse(
                        $"'{a}' ile '{b}' önek çakışması yaşarsa specialist tespiti bozulur");
    }
}
