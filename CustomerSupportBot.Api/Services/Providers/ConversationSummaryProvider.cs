// Services/Providers/ConversationSummaryProvider.cs
// Uzun konuşma geçmişini LLM ile özetleyerek token tasarrufu sağlar.

using System.Text;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Services.Providers;

/// <summary>
/// Konuşma geçmişi belirli bir eşiği aştığında, eski mesajları
/// LLM kullanarak özetler ve özet + son mesajları bağlam olarak sunar.
/// Bu sayede token limiti aşılmadan uzun konuşmalar sürdürülebilir.
/// </summary>
public class ConversationSummaryProvider : IContextProvider
{
    private readonly IChatClient _chatClient;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<ConversationSummaryProvider> _logger;

    /// <summary>Özetleme tetiklenecek minimum mesaj sayısı.</summary>
    private const int SummaryThreshold = 8;

    /// <summary>Özet sonrası saklanacak son mesaj sayısı.</summary>
    private const int RecentMessageCount = 4;

    public string Name => "ConversationSummary";
    public int Order => 5;

    public ConversationSummaryProvider(
        IChatClient chatClient,
        ISessionManager sessionManager,
        ILogger<ConversationSummaryProvider> logger)
    {
        _chatClient = chatClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<string?> GetContextAsync(AgentSession session)
    {
        var history = _sessionManager.GetHistory(session.SessionId);
        if (history.Count < SummaryThreshold)
            return null;

        // Zaten özet varsa ve çok fazla yeni mesaj yoksa mevcut özeti kullan
        if (session.State.ConversationSummary != null &&
            history.Count - SummaryThreshold < RecentMessageCount)
        {
            return FormatSummaryContext(session.State.ConversationSummary);
        }

        // Özetlenecek eski mesajlar (son RecentMessageCount mesaj hariç)
        var oldMessages = history.Take(history.Count - RecentMessageCount).ToList();
        if (oldMessages.Count == 0)
            return null;

        try
        {
            var summary = await SummarizeAsync(oldMessages);
            session.State.ConversationSummary = summary;
            _sessionManager.UpdateSession(session);

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

    private async Task<string> SummarizeAsync(List<ChatMessage> messages)
    {
        var conversationText = new StringBuilder();
        foreach (var msg in messages)
        {
            var role = msg.Role == ChatRole.User ? "Müşteri" : "Asistan";
            conversationText.AppendLine($"{role}: {msg.Text}");
        }

        var prompt = new List<ChatMessage>
        {
            new(ChatRole.System,
                "Aşağıdaki müşteri destek konuşmasını kısa ve öz bir şekilde özetle. " +
                "Önemli bilgileri koru: müşteri kimliği, sipariş numaraları, yapılan işlemler, " +
                "çözülmemiş sorunlar. Türkçe yaz. Maksimum 150 kelime."),
            new(ChatRole.User, conversationText.ToString())
        };

        var response = await _chatClient.GetResponseAsync(prompt);
        return response.Text ?? "";
    }

    private static string FormatSummaryContext(string summary)
    {
        return $"[Konuşma Özeti]\n{summary}";
    }
}

