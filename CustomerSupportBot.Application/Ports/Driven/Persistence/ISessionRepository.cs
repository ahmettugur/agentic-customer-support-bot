// Ports/Driven/Persistence/ISessionRepository.cs
// SECONDARY PORT — Oturum kalıcılığı + konuşma geçmişi.
// Postgres adaptörü: PostgresSessionManager
// InMemory adaptörü: InMemorySessionManager
//
// Bu port eski ISessionManager ve IConversationStore arayüzlerini birleştirir.
// Api projesindeki global using alias'lar geriye uyumluluğu sağlar.

using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Application.Ports.Driven.Persistence;

/// <summary>
/// Oturum bilgisi özeti (sidebar listesi için).
/// </summary>
public class SessionInfo
{
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime LastActivity { get; set; }
    public int MessageCount { get; set; }
}

/// <summary>
/// Oturum ve konuşma geçmişi kalıcılığı için secondary (driven) port.
/// Core bu port'a bağımlıdır; hangi adaptörün (Postgres/InMemory) kullanıldığını bilmez.
/// Eski ISessionManager + IConversationStore birleşik sözleşmesidir.
/// </summary>
public interface ISessionRepository
{
    // ─── Session yönetimi ───

    /// <summary>Mevcut oturumu getirir veya yeni oluşturur.</summary>
    AgentSession GetOrCreate(string? sessionId);

    /// <summary>Belirtilen oturumu döner. Bulunamazsa null.</summary>
    AgentSession? Get(string sessionId);

    /// <summary>Oturum durumunu günceller.</summary>
    void Update(AgentSession session);

    /// <summary>Admin panel için tüm aktif session'lar.</summary>
    IReadOnlyList<AgentSession> GetAll();

    /// <summary>
    /// Session state üzerinde distributed-lock korumalı mutasyon uygular.
    /// </summary>
    Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default);

    // ─── Konuşma geçmişi (eski IConversationStore) ───

    /// <summary>Belirtilen oturumun konuşma geçmişinin bir kopyasını döndürür.</summary>
    List<ChatMessage> GetHistory(string sessionId);

    /// <summary>Oturuma yeni bir kullanıcı-asistan mesaj çifti ekler.</summary>
    void AddExchange(string sessionId, string userMessage, string botResponse);

    /// <summary>
    /// Oturum geçmişine sadece bir asistan mesajı ekler (admin Live Takeover için).
    /// </summary>
    void AppendAssistantMessage(string sessionId, string text);

    /// <summary>Belirtilen oturumun geçmişini temizler.</summary>
    void ClearSession(string sessionId);

    /// <summary>Tüm oturumların özet bilgisini döndürür (sidebar listesi için).</summary>
    List<SessionInfo> GetAllSessions();

    // ─── State extraction ───

    /// <summary>Konuşma içeriğinden durum bilgilerini çıkarır ve session state'i günceller.</summary>
    void ExtractAndUpdateState(string sessionId, string userMessage, string botResponse);
}
