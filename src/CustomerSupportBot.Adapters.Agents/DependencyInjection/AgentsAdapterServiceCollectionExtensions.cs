// Adapters.Agents/DependencyInjection/AgentsAdapterServiceCollectionExtensions.cs
// Agents adapter servislerini DI container'a kaydeder.

using CustomerSupportBot.Adapters.Agents.Evaluation;
using CustomerSupportBot.Application.Ports.Inbound;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Adapters.Agents.DependencyInjection;

public static class AgentsAdapterServiceCollectionExtensions
{
    /// <summary>
    /// CustomerSupportTeam, ApprovalGateService ve bağımlılıklarını kaydeder.
    /// <para>
    /// Ön koşul: <c>IChatClient</c> (Microsoft.Extensions.AI) bu metod çağrılmadan önce
    /// DI container'a kayıtlı olmalıdır. Bu adapter IChatClient'ı Microsoft.Agents.AI
    /// framework'ünün gerektirdiği ChatClientAgent oluşturmak için kullanır.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAgentsAdapter(this IServiceCollection services)
    {
        // IChatClient'ın kayıtlı olduğunu doğrula (fail-fast)
        if (!services.Any(d => d.ServiceType == typeof(IChatClient)))
        {
            throw new InvalidOperationException(
                "IChatClient must be registered before calling AddAgentsAdapter(). " +
                "Ensure AddAiServices() is called first in the composition root.");
        }

        services.AddSingleton<ApprovalGateService>();
        services.AddSingleton<CustomerSupportTeam>();
        services.AddSingleton<IAgentTeamPort>(sp =>
            sp.GetRequiredService<CustomerSupportTeam>());

        // EvaluationRunner MAF'ın EvalItem/ChatMessage tiplerine bağımlı olduğu için
        // burada (Application değil) yaşıyor — bkz. CriteriaEvaluator.cs başındaki not.
        services.AddSingleton<EvaluationRunner>();
        services.AddSingleton<IEvaluationPort>(sp => sp.GetRequiredService<EvaluationRunner>());

        return services;
    }
}
