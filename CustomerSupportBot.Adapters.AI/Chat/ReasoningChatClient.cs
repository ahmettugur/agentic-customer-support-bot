// Adapters.AI/Chat/ReasoningChatClient.cs
// Reasoning modeli için ayrı IChatClient wrapper'ı.
// Keyed service olarak kaydedilir, ReasoningAgent tarafından kullanılır.

using CustomerSupportBot.Application.Ports.Driven.AI;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.AI.Chat;

/// <summary>
/// Reasoning model (o-series) için ayrılmış IChatClient wrapper.
/// DI'da keyed service olarak kullanılabilir — bu sayede standart chat client
/// ile reasoning chat client ayrıştırılır.
/// </summary>
public class ReasoningChatClient : IReasoningChatClient
{
    public IChatClient Client { get; }
    public string ModelName { get; }
    public string ReasoningEffort { get; }

    public ReasoningChatClient(IChatClient client, string modelName, string reasoningEffort)
    {
        Client = client;
        ModelName = modelName;
        ReasoningEffort = reasoningEffort;
    }
}
