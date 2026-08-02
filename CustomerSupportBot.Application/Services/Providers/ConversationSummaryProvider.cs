// Application/Services/Providers/ConversationSummaryProvider.cs
// Uzun konuşma geçmişini LLM ile özetleyerek token tasarrufu sağlar.

using System.Text;

using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Providers;

/// <summary>
/// Konuşma geçmişi belirli bir eşiği aştığında, eski mesajları
/// LLM kullanarak özetler ve özet + son mesajları bağlam olarak sunar.
/// </summary>
public class ConversationSummaryProvider : IContextProvider
{
    private readonly IGeneralChatClient _chatClient;
    private readonly ISessionManager _sessionRepository;
    private readonly ILogger<ConversationSummaryProvider> _logger;

    private const int SummaryThreshold = 8;
    private const int RecentMessageCount = 4;

    public string Name => "ConversationSummary";
    public int Order => 5;

    public ConversationSummaryProvider(
        IGeneralChatClient chatClient,
        ISessionManager sessionRepository,
        ILogger<ConversationSummaryProvider> logger)
    {
        _chatClient = chatClient;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<string?> GetContextAsync(AgentSession session, string currentQuery)
    {
        var history = _sessionRepository.GetHistory(session.SessionId);
        if (history.Count < SummaryThreshold)
            return null;

        if (session.State.ConversationSummary != null &&
            history.Count - SummaryThreshold < RecentMessageCount)
        {
            return FormatSummaryContext(session.State.ConversationSummary);
        }

        var oldMessages = history.Take(history.Count - RecentMessageCount).ToList();
        if (oldMessages.Count == 0)
            return null;

        try
        {
            var summary = await SummarizeAsync(oldMessages);
            session.State.ConversationSummary = summary;
            _sessionRepository.Update(session);

            _logger.LogInformation(
                "Konuşma özetlendi: {OldCount} mesaj → {SummaryLength} karakter",
                oldMessages.Count, summary.Length);

            return FormatSummaryContext(summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Konuşma özetleme başarısız oldu");
            return null;
        }
    }

    private async Task<string> SummarizeAsync(List<ConversationMessage> messages)
    {
        var conversationText = new StringBuilder();
        foreach (var msg in messages)
        {
            var role = msg.Role == ConversationRoles.User ? "Müşteri" : "Asistan";
            conversationText.AppendLine($"{role}: {msg.Text}");
        }

        var prompt = new List<ConversationMessage>
        {
            new(ConversationRoles.System,
                "Aşağıdaki müşteri destek konuşmasını kısa ve öz bir şekilde özetle. " +
                "Önemli bilgileri koru: müşteri kimliği, sipariş numaraları, yapılan işlemler, " +
                "çözülmemiş sorunlar. Türkçe yaz. Maksimum 150 kelime."),
            new(ConversationRoles.User, conversationText.ToString())
        };

        return await _chatClient.CompleteAsync(prompt);
    }

    private static string FormatSummaryContext(string summary)
        => $"[Konuşma Özeti]\n{summary}";
}
