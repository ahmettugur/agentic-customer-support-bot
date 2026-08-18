// Adapters.Agents/A2A/A2AAgentCatalog.cs
// A2A (Agent2Agent) protokolüyle DIŞ SİSTEMLERE açılan ajanlar.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents.A2A;

/// <summary>
/// A2A ile yayınlanan ajanların kurulduğu yer. Bu ajanlar, sohbet/sesli kanaldaki workflow
/// ajanlarından (<c>Team/</c> klasörü) <b>tamamen ayrı örneklerdir</b> — aynı nesneler
/// paylaşılmaz.
///
/// <para>
/// <b>Neden ayrı olmak ZORUNDA:</b> workflow ajanları
/// <c>ResponseFormat = ChatResponseFormat.ForJsonSchema&lt;SpecialistReasoningSchema&gt;()</c>
/// ile kurulur; yani kullanıcıya yönelik metin değil, boru hattının içinde ayrıştırılmak üzere
/// <b>yapılandırılmış akıl yürütme JSON'u</b> üretirler (<c>WorkflowResponseExtractor</c> onu
/// ayrıştırır, <c>ResponseAgent</c> cümleye çevirir). Onları olduğu gibi yayınlamak dış çağırana
/// ham reasoning döndürür ve <c>preToolCheck</c>, <c>postToolReflection</c>, güven skorları,
/// eskalasyon bayrakları gibi <b>iç alanları sızdırır</b>. Buradaki ajanlarda o şema yoktur —
/// düz metin üretirler.
/// </para>
///
/// <para>
/// <b>Yetki yapısaldır, koşullu değil.</b> Bu ajanlara yan etkili tool'lar (sipariş oluşturma/
/// iptal, iade talebi, şikayet kaydı) <b>hiç verilmez</b> — "dışarıdan gelen çağrı yazamaz"
/// bir <c>if</c> dalı değil, o tool'un ajanda bulunmamasıdır. Yan etkili tool kümesinin tek
/// doğruluk kaynağı <see cref="WellKnown.SideEffectToolOwners"/>'dır; <see cref="ReadOnlyOnly"/>
/// bunu çalışma zamanında doğrular, böylece ileride oraya yeni bir yazma tool'u eklenirse
/// buraya sessizce sızamaz.
/// </para>
///
/// <para>
/// <b>Kimlik:</b> sipariş tool'ları müşteri kimliğini LLM'den değil,
/// <c>IApprovalContextAccessor</c> üzerinden ambient bağlamdan alır (bkz.
/// <c>ApprovalGateService.CurrentCustomerId</c>). Bu yüzden A2A çağrısını karşılayan taraf,
/// ajanı çalıştırmadan ÖNCE <c>SetScope(...)</c> ile doğrulanmış müşteri kimliğini kurmak
/// zorundadır — tıpkı <c>ChatPortService</c>'in yaptığı gibi. Kimlik kurulmazsa tool'lar boş
/// müşteri kimliğiyle çalışır ve veri döndürmez.
/// </para>
/// </summary>
public sealed class A2AAgentCatalog
{
    /// <summary>A2A ile yayınlanan ürün ajanı — katalog sorguları, müşteri kimliği gerektirmez.</summary>
    public AIAgent Product { get; }

    /// <summary>
    /// A2A ile yayınlanan sipariş ajanı — <b>salt-okunur</b>. Çalıştırılmadan önce ambient
    /// müşteri kimliği kurulmuş olmalıdır.
    /// </summary>
    public AIAgent Order { get; }

    /// <summary>
    /// A2A ile yayınlanan şikayet ajanı — <b>salt-okunur</b>. Sipariş ajanıyla aynı kimlik
    /// ön koşuluna tabidir; şikayet KAYDI bu kanalda yoktur.
    /// </summary>
    public AIAgent Complaint { get; }

    public A2AAgentCatalog(
        IChatClient chatClient,
        IPromptRepository prompts,
        ApprovalGateService approvalGate,
        ICustomerSupportToolsService tools)
    {
        Product = WithTelemetry(new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = A2AAgentNames.Product,
            Description = "Ürün kataloğu sorguları: fiyat, stok, kategori listesi.",
            ChatOptions = new ChatOptions
            {
                Instructions = prompts.Get("agents/a2a-product-agent"),
                Tools = ReadOnlyOnly(
                    AIFunctionFactory.Create(tools.ProductInquiryTool,
                        new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductInquiry }),
                    AIFunctionFactory.Create(tools.ProductListTool,
                        new AIFunctionFactoryOptions { Name = WellKnown.ToolNames.ProductList }))
            }
        }));

        Order = WithTelemetry(new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = A2AAgentNames.Order,
            Description = "Sipariş bilgisi (salt-okunur): durum sorgulama, son sipariş, sipariş listesi.",
            ChatOptions = new ChatOptions
            {
                Instructions = prompts.Get("agents/a2a-order-agent"),
                // Bu üç builder ApprovalGateService'ten YENİDEN KULLANILIYOR (kopyalanmıyor):
                // müşteri kimliğini ambient bağlamdan alma garantisi de böylece aynen taşınıyor.
                Tools = ReadOnlyOnly(
                    approvalGate.BuildOrderStatusTool(),
                    approvalGate.BuildGetLastOrderTool(),
                    approvalGate.BuildGetAllOrdersTool())
            }
        }));

        Complaint = WithTelemetry(new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = A2AAgentNames.Complaint,
            Description = "Şikayet bilgisi (salt-okunur): durum sorgulama ve şikayet listesi.",
            ChatOptions = new ChatOptions
            {
                Instructions = prompts.Get("agents/a2a-complaint-agent"),
                // Şikayet KAYDI (complaint_registration_tool) bilerek YOK — yan etkilidir ve
                // ReadOnlyOnly bariyeri onu zaten reddederdi.
                Tools = ReadOnlyOnly(
                    approvalGate.BuildComplaintStatusTool(),
                    approvalGate.BuildGetAllComplaintsTool())
            }
        }));
    }

    /// <summary>
    /// Ajanı OpenTelemetry ile sarar — workflow ajanlarıyla AYNI ActivitySource'a yazar
    /// (<c>AgentTeamFactory.WrapWithTelemetry</c>).
    ///
    /// <para>
    /// Sarılmasaydı A2A çağrıları gözlemlenebilirlikte <b>kör nokta</b> olurdu: dış sistemlere
    /// açık, yani en çok izlenmesi gereken kanal, hiç span üretmezdi. Aynı source'a yazması
    /// kasıtlı — mevcut trace altyapısı ve panoları ek iş yapmadan bu çağrıları da görür;
    /// hangi kanaldan geldiği ajan adından (<see cref="A2AAgentNames"/>) ayırt edilir.
    /// </para>
    /// </summary>
    private static AIAgent WithTelemetry(AIAgent agent)
        => agent.AsBuilder().UseOpenTelemetry(TelemetryConstants.ActivitySourceName).Build();

    /// <summary>
    /// Verilen tool'ların hiçbirinin yan etkili olmadığını doğrular ve listeyi aynen döner.
    ///
    /// <para>
    /// Yorum yerine çalışma zamanı kontrolü olmasının sebebi: yan etkili tool listesi
    /// (<see cref="WellKnown.SideEffectToolOwners"/>) ileride büyüyecek ve o an bu dosyayı
    /// kimse açmayacak. Kontrol olmasaydı, yeni bir yazma tool'u buraya yanlışlıkla eklendiğinde
    /// hata ancak dış bir sistem onu çağırdığında — yani üretimde — fark edilirdi.
    /// </para>
    /// </summary>
    internal static IList<AITool> ReadOnlyOnly(params AIFunction[] functions)
    {
        foreach (var fn in functions)
        {
            if (WellKnown.SideEffectToolOwners.ContainsKey(fn.Name))
            {
                throw new InvalidOperationException(
                    $"A2A ajanına yan etkili tool verilemez: '{fn.Name}'. Bu kanal salt-okunurdur; " +
                    "yazma işlemleri yalnızca kimliği doğrulanmış sohbet/sesli kanalda, HITL onayıyla yapılır.");
            }
        }
        return functions.Cast<AITool>().ToList();
    }
}

/// <summary>
/// A2A ile yayınlanan ajanların adları. Sabit tutulur: hem <c>AddA2AServer</c> kaydında hem
/// endpoint/AgentCard tarafında aynı ad kullanılır, serbest metin olarak iki yere yazılırsa
/// biri değiştiğinde köprü çalışma zamanında kopar.
/// </summary>
public static class A2AAgentNames
{
    public const string Product = "ProductInfoAgent";
    public const string Order = "OrderInfoAgent";
    public const string Complaint = "ComplaintInfoAgent";
}
