// Services/ReasoningChatClient.cs
// Reasoning modeli için ayrı IChatClient wrapper'ı.
// Keyed service olarak kaydedilir, ReasoningAgent tarafından kullanılır.

using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Services;

/// <summary>
/// Reasoning model (o-series) için ayrılmış IChatClient wrapper.
/// DI'da keyed service olarak kullanılabilir — bu sayede standart chat client
/// Ile reasoning chat client ayrıştırılır.
/// </summary>
public class ReasoningChatClient
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
