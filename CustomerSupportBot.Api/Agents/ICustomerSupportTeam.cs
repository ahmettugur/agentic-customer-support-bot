// Agents/ICustomerSupportTeam.cs
// Müşteri destek ajan takımının dış arayüzü — test/mock için.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Agents;

/// <summary>
/// Müşteri destek ajan takımının dış davranış sözleşmesi.
/// Caller'lar (orchestrators, endpoints, evaluation runner) bu arayüze bağımlı olmalı.
/// </summary>
public interface ICustomerSupportTeam
{
    /// <summary>
    /// Kullanıcı sorgusunu workflow'da koşturur ve nihai yanıtı döndürür (non-streaming).
    /// </summary>
    Task<string> RunAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null);

    /// <summary>
    /// Workflow'u SSE stream event'leri olarak koşturur.
    /// </summary>
    IAsyncEnumerable<StreamEvent> RunStreamingAsync(
        string query,
        List<ChatMessage>? conversationHistory = null,
        AgentSession? session = null,
        ReasoningResult? reasoning = null,
        CancellationToken ct = default);
}

