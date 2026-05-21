// Extensions/AiServicesExtensions.cs
// Composition Root: AI + Telemetry adapter'larının IChatClient kayıtlarını birleştirir.
// Bu dosya bilinçli olarak iki adapter'ın concern'lerini (AI client oluşturma + telemetri
// dekorasyon) tek noktada birleştirir — Composition Root'un sorumluluk alanıdır.
// IChatClient hem Agents adapter (CustomerSupportTeam) hem de AI adapter (IGeneralChatClient)
// tarafından tüketilir.

using CustomerSupportBot.Adapters.AI;
using CustomerSupportBot.Adapters.AI.Chat;
using CustomerSupportBot.Adapters.AI.DependencyInjection;
using CustomerSupportBot.Adapters.Telemetry;
using CustomerSupportBot.Adapters.Telemetry.Chat;
using CustomerSupportBot.Adapters.Telemetry.OpenTelemetry;
using CustomerSupportBot.Application.Ports.Driven.AI;
using CustomerSupportBot.Application.Ports.Driven.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.Extensions;

public static class AiServicesExtensions
{
    public static IServiceCollection AddAiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAiAdapters(configuration);

        services.AddSingleton<IChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            var inner = AiClientFactory.CreateStandardChatClient(options);
            return WrapWithTelemetry(sp, inner, ResolveStandardModel(options), options.Provider.ToString());
        });

        services.AddSingleton<ReasoningChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return AiClientFactory.CreateReasoningChatClient(options, inner =>
                WrapWithTelemetry(sp, inner, ResolveReasoningModel(options), options.Provider.ToString()));
        });
        services.AddSingleton<IReasoningChatClient>(sp =>
            sp.GetRequiredService<ReasoningChatClient>());

        services.AddSingleton<IGeneralChatClient>(sp =>
            new GeneralChatClientAdapter(sp.GetRequiredService<IChatClient>()));

        return services;
    }

    private static IChatClient WrapWithTelemetry(
        IServiceProvider sp, IChatClient inner, string modelHint, string provider)
    {
        var telemetryOptions = sp.GetRequiredService<IOptions<TelemetryOptions>>().Value;
        if (!telemetryOptions.Enabled) return inner;

        return new TelemetryChatClient(
            inner,
            sp.GetRequiredService<ICostCalculatorPort>(),
            sp.GetRequiredService<CostUsageStore>(),
            modelHint,
            provider,
            sp.GetRequiredService<ILogger<TelemetryChatClient>>());
    }

    private static string ResolveStandardModel(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => options.AzureOpenAI.Deployment ?? "(unknown)",
        AiProvider.Anthropic   => options.Anthropic.Model ?? "(unknown)",
        _                      => options.OpenAI.Model ?? "(unknown)"
    };

    private static string ResolveReasoningModel(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => options.AzureOpenAI.ReasoningDeployment ?? options.AzureOpenAI.Deployment ?? "(unknown)",
        AiProvider.Anthropic   => options.Anthropic.ReasoningModel ?? options.Anthropic.Model ?? "(unknown)",
        _                      => options.OpenAI.ReasoningModel ?? options.OpenAI.Model ?? "(unknown)"
    };
}

