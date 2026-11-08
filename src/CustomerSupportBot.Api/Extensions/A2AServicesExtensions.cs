// Api/Extensions/A2AServicesExtensions.cs
// A2A ajanlarının SERVİS KAYDI. Endpoint yayınlama ayrı dosyada: Endpoints/A2AEndpoints.cs.

using CustomerSupportBot.Adapters.Agents.A2A;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Hosting.A2A;

namespace CustomerSupportBot.Api.Extensions;

public static class A2AServicesExtensions
{
    /// <summary>
    /// A2A ajanlarını MAF'ın hosting kaydına bağlar. Ajan örnekleri <see cref="A2AAgentCatalog"/>'dan
    /// gelir — burada YENİDEN KURULMAZ, çünkü salt-okunur tool bariyeri orada yaşıyor.
    ///
    /// <para>
    /// Kayıt <b>ad üzerinden</b> yapılır (<c>AddAIAgent(name, factory)</c>): ajanlar DI'dan
    /// (<c>IChatClient</c>, prompt deposu, <c>ApprovalGateService</c>) beslendiği için servis
    /// kaydı anında henüz çözülemezler. Ad tabanlı geç bağlama bu tavuk-yumurta sorununu çözer;
    /// endpoint tarafı da aynı adı kullanır (<see cref="A2AAgentNames"/>).
    /// </para>
    /// </summary>
    public static IServiceCollection AddA2AAgents(this IServiceCollection services)
    {
        // A2A contextId/taskId değerleri yalnızca konuşmayı sürdürme anahtarıdır; sahiplik
        // kanıtı değildir. Hosting store'larını çağıranın NameIdentifier claim'iyle bölümle.
        // Subject token'larda bu claim a2a:{partner}:{customer}, partner token'larında ise
        // benzersiz kullanıcı ID'sidir. Böylece aynı wire contextId farklı principal'lar
        // arasında oturum taşımak için kullanılamaz.
        services.AddHttpContextAccessor();
        services.UseClaimsBasedAgentIsolation();

        // Yanıt her zaman tek bir AgentMessage'dır (AgentRunMode.ReturnMessage): arka plan (uzun
        // süreli, AgentTask olarak pollanan) görevler dış çağıranın sunucuda iş biriktirmesine izin
        // verirdi. Bu kanal salt-okunur sorgular içindir — her çağrı istek ömrü içinde başlar ve biter.
        //
        // 1.28'de bu artık bir seçenek değil: AddA2AServer'ın kurduğu handler sabit olarak
        // ReturnMessage kullanır (paket kodundan doğrulandı); kayıt seçeneklerindeki AgentRunMode
        // kaldırıldı. Başka bir mod ancak ajan adıyla keyed bir IAgentHandler kaydedilerek seçilir —
        // bu projede bilerek yapılmıyor. Sürüm yükseltmelerinde bu varsayılanın değişmediği,
        // A2AEndpointsTests'teki "tek message olayı" sözleşmesiyle de denetlenir.
        services
            .AddAIAgent(A2AAgentNames.Product,
                (sp, _) => sp.GetRequiredService<A2AAgentCatalog>().Product)
            .AddA2AServer();

        services
            .AddAIAgent(A2AAgentNames.Order,
                (sp, _) => sp.GetRequiredService<A2AAgentCatalog>().Order)
            .AddA2AServer();

        services
            .AddAIAgent(A2AAgentNames.Complaint,
                (sp, _) => sp.GetRequiredService<A2AAgentCatalog>().Complaint)
            .AddA2AServer();

        return services;
    }
}
