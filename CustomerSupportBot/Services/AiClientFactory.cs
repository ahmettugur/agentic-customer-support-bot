// Services/AiClientFactory.cs
// OpenAI, Azure OpenAI ve Anthropic sağlayıcıları arasında geçişi yöneten factory.
// Strongly-typed AiOptions üzerinden çalışır (IOptions<AiOptions> ile DI'dan gelir).

using System.ClientModel;
using Anthropic;
using Azure.AI.OpenAI;
using CustomerSupportBot.Models;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CustomerSupportBot.Services;

/// <summary>
/// AiOptions'a göre standart ve reasoning chat client'larını üretir.
/// </summary>
public static class AiClientFactory
{
    /// <summary>Standart sohbet için IChatClient üretir.</summary>
    public static IChatClient CreateStandardChatClient(AiOptions options) => options.Provider switch
    {
        AiProvider.AzureOpenAI => CreateAzureChatClient(options.AzureOpenAI, Require(options.AzureOpenAI.Deployment, "AI:AzureOpenAI:Deployment")),
        AiProvider.Anthropic => CreateAnthropicChatClient(options.Anthropic, Require(options.Anthropic.Model, "AI:Anthropic:Model")),
        _ => CreateOpenAIChatClient(options.OpenAI, Require(options.OpenAI.Model, "AI:OpenAI:Model")),
    };

    /// <summary>Reasoning model wrapper'ı üretir.</summary>
    public static ReasoningChatClient CreateReasoningChatClient(AiOptions options)
    {
        switch (options.Provider)
        {
            case AiProvider.AzureOpenAI:
            {
                var az = options.AzureOpenAI;
                var deployment = !string.IsNullOrWhiteSpace(az.ReasoningDeployment) ? az.ReasoningDeployment! : Require(az.Deployment, "AI:AzureOpenAI:Deployment");
                var client = CreateAzureChatClient(az, deployment);
                return new ReasoningChatClient(client, deployment, Require(az.ReasoningEffort, "AI:AzureOpenAI:ReasoningEffort"));
            }
            case AiProvider.Anthropic:
            {
                var an = options.Anthropic;
                var model = !string.IsNullOrWhiteSpace(an.ReasoningModel) ? an.ReasoningModel! : Require(an.Model, "AI:Anthropic:Model");
                var client = CreateAnthropicChatClient(an, model);
                // Anthropic'te ayrı bir reasoning effort kavramı yoktur — etiket olarak medium taşınır.
                return new ReasoningChatClient(client, model, "medium");
            }
            default:
            {
                var oa = options.OpenAI;
                var model = !string.IsNullOrWhiteSpace(oa.ReasoningModel) ? oa.ReasoningModel! : Require(oa.Model, "AI:OpenAI:Model");
                var client = CreateOpenAIChatClient(oa, model);
                return new ReasoningChatClient(client, model, Require(oa.ReasoningEffort, "AI:OpenAI:ReasoningEffort"));
            }
        }
    }

    private static string Require(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value!
            : throw new InvalidOperationException($"{key} yapılandırması bulunamadı (appsettings.json'da boş veya tanımsız).");

    // ─── Internals ───

    private static IChatClient CreateOpenAIChatClient(OpenAiOptions options, string model)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "AI:OpenAI:ApiKey yapılandırması bulunamadı. appsettings.json içinde ayarlanmalı veya AI:Provider farklı bir sağlayıcıya değiştirilmeli.");
        }

        return new OpenAIClient(options.ApiKey).GetChatClient(model).AsIChatClient();
    }

    private static IChatClient CreateAzureChatClient(AzureOpenAiOptions options, string deployment)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException("AI:AzureOpenAI:Endpoint yapılandırması bulunamadı.");
        }
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("AI:AzureOpenAI:ApiKey yapılandırması bulunamadı.");
        }

        var azureClient = new AzureOpenAIClient(
            new Uri(options.Endpoint!),
            new ApiKeyCredential(options.ApiKey!));

        return azureClient.GetChatClient(deployment).AsIChatClient();
    }

    private static IChatClient CreateAnthropicChatClient(AnthropicOptions options, string model)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("AI:Anthropic:ApiKey yapılandırması bulunamadı.");
        }

        var client = new AnthropicClient { ApiKey = options.ApiKey };
        return client.AsIChatClient(model, options.MaxTokens);
    }
}
