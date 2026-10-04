// Application/Ports/Inbound/IAgentAssistPort.cs
// Temsilci asistanı — devralınan sohbet için özet, bağlam ve yanıt taslağı.

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Bir sohbeti devralan temsilciye yardım: konuşma özeti, müşterinin şu anki talebi, düzenlenebilir
/// yanıt taslağı ve LLM gerektirmeyen bağlam (duygu, profil, ilgili makaleler, açık işler).
/// Taslak hiçbir zaman otomatik gönderilmez — temsilci karar verir.
/// </summary>
public interface IAgentAssistPort
{
    /// <summary>Oturum yoksa <c>null</c>.</summary>
    Task<AgentAssistResult?> GetAssistAsync(string sessionId, CancellationToken ct = default);
}

/// <param name="Summary">Konuşma özeti (LLM). Üretilemezse <c>null</c>.</param>
/// <param name="CustomerRequest">Müşterinin şu anki talebi, tek cümle (LLM).</param>
/// <param name="SuggestedReply">Temsilcinin gözden geçireceği yanıt taslağı (LLM).</param>
/// <param name="AssistError">LLM bölümü üretilemediyse kullanıcıya gösterilecek açıklama.</param>
public sealed record AgentAssistResult(
    string SessionId,
    string? Summary,
    string? CustomerRequest,
    string? SuggestedReply,
    string? AssistError,
    AgentAssistSentiment Sentiment,
    AgentAssistProfile? Profile,
    IReadOnlyList<AgentAssistArticle> Articles,
    IReadOnlyList<AgentAssistOpenItem> OpenItems);

public sealed record AgentAssistSentiment(string? Label, double Score, int ConsecutiveNegativeTurns);

public sealed record AgentAssistProfile(
    string CustomerId,
    string? Summary,
    string PreferredTone,
    IReadOnlyList<string> ProductInterests,
    IReadOnlyList<string> TopIntents,
    double? AverageRating,
    int TotalSessions);

public sealed record AgentAssistArticle(string Title, string Snippet, string? Source, double Score);

/// <param name="Kind"><c>escalation</c> veya <c>approval</c>.</param>
/// <param name="Description">Eskalasyonda gerekçe, onayda işlem (tool) adı.</param>
public sealed record AgentAssistOpenItem(string Kind, string Id, string Description, string Status, DateTime CreatedAt);
