// Adapters.Agents/DependencyInjection/AgentsAdapterServiceCollectionExtensions.cs
// Agents adapter servislerini DI container'a kaydeder.

using CustomerSupportBot.Application.Ports.Driven;
using Microsoft.Extensions.DependencyInjection;

namespace CustomerSupportBot.Adapters.Agents.DependencyInjection;

public static class AgentsAdapterServiceCollectionExtensions
{
    /// <summary>
    /// CustomerSupportTeam, ApprovalGateService ve bağımlılıklarını kaydeder.
    /// </summary>
    public static IServiceCollection AddAgentsAdapter(this IServiceCollection services)
    {
        services.AddSingleton<ApprovalGateService>();
        services.AddSingleton<CustomerSupportTeam>();
        services.AddSingleton<IAgentTeamPort>(sp =>
            sp.GetRequiredService<CustomerSupportTeam>());

        return services;
    }
}
