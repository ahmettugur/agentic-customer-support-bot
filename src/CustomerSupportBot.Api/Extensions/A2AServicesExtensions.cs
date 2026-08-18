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
        // AgentRunMode.DisallowBackground bilinçli: arka plan (uzun süreli) görevler dış
        // çağıranın sunucuda iş biriktirmesine izin verirdi. Bu kanal salt-okunur sorgular
        // içindir — her çağrı istek ömrü içinde başlar ve biter.
        //
        // MEAI001 yalnızca BURADA bastırılıyor (proje genelinde değil): AgentRunMode henüz
        // deneysel işaretli. Bastırmayı dar tutmak, aynı uyarının başka bir deneysel API
        // kullanıldığında hâlâ görünmesini sağlar. Sürüm yükseltmelerinde bu pragma'nın
        // hâlâ gerekli olup olmadığı kontrol edilmeli.
#pragma warning disable MEAI001
        services
            .AddAIAgent(A2AAgentNames.Product,
                (sp, _) => sp.GetRequiredService<A2AAgentCatalog>().Product)
            .AddA2AServer(o => o.AgentRunMode = AgentRunMode.DisallowBackground);

        services
            .AddAIAgent(A2AAgentNames.Order,
                (sp, _) => sp.GetRequiredService<A2AAgentCatalog>().Order)
            .AddA2AServer(o => o.AgentRunMode = AgentRunMode.DisallowBackground);

        services
            .AddAIAgent(A2AAgentNames.Complaint,
                (sp, _) => sp.GetRequiredService<A2AAgentCatalog>().Complaint)
            .AddA2AServer(o => o.AgentRunMode = AgentRunMode.DisallowBackground);
#pragma warning restore MEAI001

        return services;
    }
}
