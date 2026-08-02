// Tests/Spikes/WorkflowBuilderSpikeTests.cs
// K3 Faz 3 SPIKE (go/no-go denemesi) — plastic-man-vision-lockjaw.md planındaki "elle
// WorkflowBuilder ile mini graf" sorularını yanıtlamak için. Amaç ÜRETİM KODU DEĞİL: bu dosya
// production graph'a hiç bağlı değil, sadece MAF 1.15.0'ın gerçek API yüzeyine karşı derlenip
// derlenmediğini ve GroupChatWorkflowBuilder olmadan bir workflow'un kurulup kurulamadığını
// kanıtlıyor. Canlı bir LLM çağrısı gerektiren senaryolar (appsettings.json'da API key yok bu
// ortamda) [Skip] ile işaretli — yalnızca derleme/yapı doğrulaması amaçlı, gerçek çalıştırma
// ayrı bir ortamda elle yapılmalı.
//
// Yanıtlanan sorular (bkz. plan §Faz 3 SPIKE):
//   1. AIAgentHostExecutor düz grafta (GroupChatHost'suz) çalışıyor mu, TurnToken gerekli mi?
//      → EVET çalışıyor: AIAgent.BindAsExecutor(AIAgentHostOptions?) PUBLIC bir extension —
//        AIAgentHostExecutor'ın kendisi internal ama BindAsExecutor() onu GroupChat'e hiç
//        ihtiyaç duymadan bir WorkflowBuilder graph'ına bağlıyor. Aynı AIAgentHostOptions tipi
//        GroupChat'in de kullandığı tip — davranış farkı yok.
//   2. ApprovalRequiredAIFunction → RequestInfoEvent düz grafta da çalışıyor mu?
//      → Güçlü kanıt: AIAgentHostOptions.InterceptUserInputRequests default'u false (GroupChat'in
//        kullandığıyla AYNI default) — yani ToolApprovalRequestContent yine RequestInfoEvent
//        olarak workflow'u duraklatacak, GroupChat'e özgü bir davranış değil. Canlı doğrulama
//        (gerçek approval bekletme) bu ortamda yapılamadı (API key yok) — production'a geçmeden
//        önce canlı test ZORUNLU.
//   3. workflow.WithOpenTelemetry(cfg, activitySource) span üretiyor mu?
//      → API YÜZEYİNDE MEVCUT ve bu spike'ta gerçekten çağrılıp derleniyor: WorkflowBuilder.
//        WithOpenTelemetry(Action<WorkflowTelemetryOptions>?, ActivitySource?) public extension.
//        GroupChatWorkflowBuilder.Build() bunu hiç açığa çıkarmıyordu (önceki bulgu) — düz
//        WorkflowBuilder'da bu artık MÜMKÜN. Bu, Faz 3'ün somut, doğrulanmış bir kazanımı.
//   4. Executor event'leri (ExecutorInvoked/Completed) UI rozetleri için yeterli mi?
//      → Aynı AIAgentHostExecutor kullanıldığı için WorkflowRunner'ın bugün zaten tükettiği
//        event tipleri (ExecutorInvokedEvent/ExecutorCompletedEvent/AgentResponseUpdateEvent/
//        WorkflowOutputEvent) burada da üretilir — GroupChat'e özgü bir event tipi yok.
//
// SONUÇ: Spike'ın statik/derleme-zamanı kısmı GO yönünde. Kalan tek gerçek risk madde 2'nin
// CANLI doğrulanmamış olması — production geçişi öncesi gerçek bir approval-bekletme testi şart.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using System.Diagnostics;

namespace CustomerSupportBot.Api.Tests.Spikes;

public class WorkflowBuilderSpikeTests
{
    /// <summary>
    /// Soru 1 + 3: BindAsExecutor() ile GroupChatHost olmadan bir graf kurulabiliyor mu,
    /// WithOpenTelemetry düz WorkflowBuilder'a eklenebiliyor mu? İkisi de derleniyor VE
    /// workflow.Build() hatasız dönüyor — bu saf yapısal bir doğrulama, LLM çağrısı yok.
    /// </summary>
    [Fact]
    public void RawWorkflowBuilder_BindAgentsWithoutGroupChat_BuildsSuccessfully()
    {
        var chatClient = Substitute.For<IChatClient>();

        var planningAgent = new ChatClientAgent(chatClient, instructions: "planning", name: "PlanningAgent");
        var orderAgent = new ChatClientAgent(chatClient, instructions: "order", name: "OrderAgent");
        var responseAgent = new ChatClientAgent(chatClient, instructions: "response", name: "ResponseAgent");

        // Soru 1: BindAsExecutor — GroupChatWorkflowBuilder'a hiç ihtiyaç yok.
        ExecutorBinding planningBinding = planningAgent.BindAsExecutor();
        ExecutorBinding orderBinding = orderAgent.BindAsExecutor();
        ExecutorBinding responseBinding = responseAgent.BindAsExecutor();

        var builder = new WorkflowBuilder(planningBinding)
            .AddEdge(planningBinding, orderBinding)
            .AddEdge(orderBinding, responseBinding);

        // Soru 3: GroupChatWorkflowBuilder.Build() bunu hiç açığa çıkarmıyordu — düz
        // WorkflowBuilder'da API yüzeyinde mevcut ve çağrılabiliyor.
        using var activitySource = new ActivitySource("spike-test");
        builder = builder.WithOpenTelemetry(cfg => cfg.EnableSensitiveData = false, activitySource);

        var workflow = builder.WithOutputFrom(responseBinding).Build();

        workflow.Should().NotBeNull();
    }

    /// <summary>
    /// Soru 1 (devamı): AddSwitch/AddCase ile PlanRoutingStrategy'nin yaptığı koşullu
    /// yönlendirmenin (selectedAgent'a göre specialist seçimi) düz grafta karşılığı kurulabiliyor
    /// mu? Yapısal olarak evet — gerçek PlanningResult ayrıştırması yerine basit bir string
    /// predicate ile derlenebilirlik kanıtlanıyor.
    /// </summary>
    [Fact]
    public void RawWorkflowBuilder_AddSwitchForRouting_BuildsSuccessfully()
    {
        var chatClient = Substitute.For<IChatClient>();
        var planningAgent = new ChatClientAgent(chatClient, instructions: "planning", name: "PlanningAgent");
        var orderAgent = new ChatClientAgent(chatClient, instructions: "order", name: "OrderAgent");
        var productAgent = new ChatClientAgent(chatClient, instructions: "product", name: "ProductAgent");
        var responseAgent = new ChatClientAgent(chatClient, instructions: "response", name: "ResponseAgent");

        ExecutorBinding planningBinding = planningAgent.BindAsExecutor();
        ExecutorBinding orderBinding = orderAgent.BindAsExecutor();
        ExecutorBinding productBinding = productAgent.BindAsExecutor();
        ExecutorBinding responseBinding = responseAgent.BindAsExecutor();

        var builder = new WorkflowBuilder(planningBinding)
            .AddSwitch(planningBinding, sw => sw
                // Gerçek migrasyonda predicate PlanningResultParser.TryParse(messages) ile
                // selectedAgent'a bakardı — burada yapısal kanıt için basitleştirildi.
                .AddCase<IEnumerable<ChatMessage>>(_ => true, orderBinding)
                .WithDefault(productBinding))
            .AddEdge(orderBinding, responseBinding)
            .AddEdge(productBinding, responseBinding);

        var workflow = builder.WithOutputFrom(responseBinding).Build();

        workflow.Should().NotBeNull();
    }

    /// <summary>
    /// Soru 2: ApprovalRequiredAIFunction'ın düz grafta da RequestInfoEvent üretip üretmediği —
    /// gerçek bir LLM'in tool çağırmasını gerektirdiği için bu ortamda (appsettings.json'da API
    /// key yok) çalıştırılamıyor. AIAgentHostOptions.InterceptUserInputRequests default'unun
    /// GroupChat ile AYNI (false) olduğu decompile ile doğrulandı (bkz. dosya başı yorumu) —
    /// ama bu SADECE dolaylı kanıt. Production geçişi öncesi gerçek API key'li bir ortamda bu
    /// testin [Skip] kaldırılıp çalıştırılması ZORUNLU.
    /// </summary>
    [Fact(Skip = "Gerçek LLM + tool-call gerektirir; bu ortamda API key yok. Faz 3 migrasyonu " +
                 "öncesi gerçek bir ortamda çalıştırılıp doğrulanmalı (spike go/no-go'nun tek " +
                 "canlı-doğrulanmamış maddesi).")]
    public async Task RawWorkflowBuilder_ApprovalRequiredTool_PausesWithRequestInfoEvent()
    {
        await Task.CompletedTask;
    }
}
