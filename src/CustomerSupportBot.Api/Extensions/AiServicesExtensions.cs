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
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Services.Chat;
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
            var standard = WrapWithTelemetry(sp, inner, ResolveStandardModel(options), options.Provider.ToString());

            // Model yönlendirme: hızlı model yapılandırılmışsa her çağrı turun katmanına göre seçilir. Hızlı istemci KENDİ
            // modeliyle telemetriye/bütçeye sarılır — maliyet doğru modelin fiyatıyla hesaplanır.
            if (!sp.GetRequiredService<IOptions<ModelRoutingOptions>>().Value.Enabled) return standard;
            var fastInner = AiClientFactory.CreateFastChatClient(options);
            if (fastInner is null) return standard;
            var fast = WrapWithTelemetry(sp, fastInner, ResolveFastModel(options), options.Provider.ToString());
            return new ModelRoutingChatClient(standard, fast, sp.GetRequiredService<ITurnModelTier>());
        });

        services.AddSingleton<ReasoningChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            return AiClientFactory.CreateReasoningChatClient(options, inner =>
            {
                var wrapped = WrapWithTelemetry(sp, inner, ResolveReasoningModel(options), options.Provider.ToString());
                var usage = sp.GetService<ITurnUsageTracker>();
                return usage is null ? wrapped : new CustomerSupportBot.Adapters.Agents.UsageLabelingChatClient(wrapped, "Reasoning", usage);
            });
        });
        services.AddSingleton<IReasoningChatClient>(sp =>
            sp.GetRequiredService<ReasoningChatClient>());

        services.AddSingleton<IGeneralChatClient>(sp =>
            new GeneralChatClientAdapter(sp.GetRequiredService<IChatClient>()));

        // Fotoğraf eklerinin açıklaması — standart sohbet modeli görüntü içeriğiyle çağrılır.
        services.AddSingleton<IImageAnalysisPort>(sp => new ChatClientImageAnalysisAdapter(
            sp.GetRequiredService<IChatClient>(),
            sp.GetRequiredService<CustomerSupportBot.Application.Ports.Outbound.IPromptRepository>()));

        return services;
    }

    /// <summary>
    /// Harcama limiti en dışta: limit aşıldıysa çağrı telemetriye "başarısız LLM çağrısı" olarak da düşmez.
    /// Limit telemetriden bağımsızdır (Telemetry:Enabled kapalıyken de uygulanır); LlmBudget:Enabled
    /// kapalıyken sarmalayıcı yalnızca bir seçenek okumasıdır.
    /// </summary>
    private static IChatClient WrapWithTelemetry(
        IServiceProvider sp, IChatClient inner, string modelHint, string provider) =>
        new SpendLimitChatClient(
            WrapWithUsageTelemetry(sp, inner, modelHint, provider),
            sp.GetRequiredService<ILlmSpendGuard>(),
            sp.GetRequiredService<ICostCalculatorPort>(),
            modelHint,
            provider,
            sp.GetService<ILlmCallAttribution>());

    private static IChatClient WrapWithUsageTelemetry(
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
            sp.GetRequiredService<ILogger<TelemetryChatClient>>(),
            sp.GetService<ILlmCallPersistencePort>(),
            sp.GetService<ILlmCallAttribution>(),
            BuildTraceEnrichment(sp, telemetryOptions));
    }

    /// <summary>
    /// Langfuse zenginleştirmesi: müşteri kimliği ajan turunun ortam bağlamından (onay kapsamı) okunur;
    /// içerik yalnızca <c>Telemetry:CaptureLlmContent</c> açıkken ve PII maskelenerek yazılır.
    /// </summary>
    private static LlmTraceEnrichment BuildTraceEnrichment(IServiceProvider sp, TelemetryOptions options)
    {
        var approvalContext = sp.GetService<IApprovalContextAccessor>();
        return new LlmTraceEnrichment
        {
            CaptureContent = options.CaptureLlmContent,
            MaxContentChars = Math.Max(500, options.MaxLlmContentChars),
            Redact = InputGuard.RedactPii,
            CurrentUserId = () => approvalContext?.Context?.CustomerId,
            PromptVersion = sp.GetService<IPromptRepository>()?.SetVersion
        };
    }

    private static string ResolveStandardModel(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => options.AzureOpenAI.Deployment ?? "(unknown)",
        _                      => options.OpenAI.Model ?? "(unknown)"
    };

    private static string ResolveFastModel(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => options.AzureOpenAI.FastDeployment ?? "(unknown)",
        _                      => options.OpenAI.FastModel ?? "(unknown)"
    };

    private static string ResolveReasoningModel(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => options.AzureOpenAI.ReasoningDeployment ?? options.AzureOpenAI.Deployment ?? "(unknown)",
        _                      => options.OpenAI.ReasoningModel ?? options.OpenAI.Model ?? "(unknown)"
    };
}

