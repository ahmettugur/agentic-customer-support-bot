using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Extensions;

public static class AiServicesExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        services.AddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return AiClientFactory.CreateStandardChatClient(options);
        });

        services.AddSingleton<ReasoningChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return AiClientFactory.CreateReasoningChatClient(options);
        });

        return services;
    }
}
