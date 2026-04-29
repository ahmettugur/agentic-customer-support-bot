// Services/ConversationStore.cs
// Konuşma geçmişini oturum bazında yöneten servis.
// Şimdilik in-memory (ConcurrentDictionary), ileride persistence (DB/Redis) ile değiştirilebilir.

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Services;

/// <summary>
/// Konuşma geçmişi yönetim arayüzü.
/// Persistence geçişinde sadece bu arayüzün yeni implementasyonu yazılacak.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Belirtilen oturumun konuşma geçmişinin bir kopyasını döndürür.
    /// Oturum bulunamazsa boş liste döner.
    /// </summary>
    List<ChatMessage> GetHistory(string sessionId);

    /// <summary>
    /// Oturuma yeni bir kullanıcı-asistan mesaj çifti ekler.
    /// </summary>
    void AddExchange(string sessionId, string userQuery, string assistantResponse);
    /// <summary>
    /// Oturum geçmişine sadece bir asistan mesajı ekler (admin Live Takeover
    /// yanıtları için). Son mesaj boş bir asistan turn'ü ise onu günceller;
    /// değilse yeni bir assistant message append eder.
    /// </summary>
    void AppendAssistantMessage(string sessionId, string text);
    /// <summary>
    /// Belirtilen oturumun geçmişini temizler.
    /// </summary>
    void ClearSession(string sessionId);

    /// <summary>
    /// Tüm oturumların özet bilgisini döndürür (sidebar listesi için).
    /// </summary>
    List<SessionInfo> GetAllSessions();
}

/// <summary>
/// Oturum özet bilgisi.
/// </summary>
public class SessionInfo
{
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime LastActivity { get; set; }
    public int MessageCount { get; set; }
}

/// <summary>
/// Bellek içi konuşma geçmişi deposu.
/// Uygulama yeniden başlatılana kadar veriler bellekte kalır.
/// Thread-safe erişim için ConcurrentDictionary + lock kullanılır.
/// </summary>
public class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _sessions = new();
    private readonly ConcurrentDictionary<string, SessionInfo> _sessionMeta = new();

    public List<ChatMessage> GetHistory(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var history))
        {
            lock (history)
            {
                return new List<ChatMessage>(history);
            }
        }

        return new List<ChatMessage>();
    }

    public void AddExchange(string sessionId, string userQuery, string assistantResponse)
    {
        var history = _sessions.GetOrAdd(sessionId, _ => new List<ChatMessage>());
        int count;
        lock (history)
        {
            history.Add(new ChatMessage(ChatRole.User, userQuery));
            history.Add(new ChatMessage(ChatRole.Assistant, assistantResponse));
            count = history.Count;
        }

        _sessionMeta.AddOrUpdate(sessionId,
            _ => new SessionInfo
            {
                SessionId = sessionId,
                Title = userQuery.Length > 50 ? userQuery[..50] + "..." : userQuery,
                LastActivity = DateTime.Now,
                MessageCount = count
            },
            (_, existing) =>
            {
                existing.LastActivity = DateTime.Now;
                existing.MessageCount = count;
                return existing;
            });
    }

    public void ClearSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _sessionMeta.TryRemove(sessionId, out _);
    }

    public void AppendAssistantMessage(string sessionId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var history = _sessions.GetOrAdd(sessionId, _ => new List<ChatMessage>());
        int count;
        lock (history)
        {
            // Son mesaj boş bir assistant ise onu doldur (bot karar veremedi /
            // Human mode placeholder'ı idi). Aksi halde yeni assistant mesajı ekle.
            if (history.Count > 0
                && history[^1].Role == ChatRole.Assistant
                && string.IsNullOrEmpty(history[^1].Text))
            {
                history[^1] = new ChatMessage(ChatRole.Assistant, text);
            }
            else
            {
                history.Add(new ChatMessage(ChatRole.Assistant, text));
            }
            count = history.Count;
        }

        if (_sessionMeta.TryGetValue(sessionId, out var meta))
        {
            meta.LastActivity = DateTime.Now;
            meta.MessageCount = count;
        }
    }

    public List<SessionInfo> GetAllSessions()
    {
        return _sessionMeta.Values
            .OrderByDescending(s => s.LastActivity)
            .ToList();
    }
}
