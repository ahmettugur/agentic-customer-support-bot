using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.Persistence;

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
/// Tamamen async — Postgres implementasyonu bu sayede sync-over-async
/// (.GetAwaiter().GetResult()) blocking'e ihtiyaç duymaz.
///</summary>
public interface ISessionManager
{
    // ─── Session yönetimi ───

    Task<AgentSession> GetOrCreateAsync(string? sessionId, CancellationToken ct = default);
    Task<AgentSession?> GetAsync(string sessionId, CancellationToken ct = default);
    Task UpdateAsync(AgentSession session, CancellationToken ct = default);
    Task<IReadOnlyList<AgentSession>> GetAllAsync(CancellationToken ct = default);
    Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default);

    // ─── Konuşma geçmişi ───

    Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default);
    /// <summary>
    /// Bir konuşma turunu (kullanıcı + bot mesajı) geçmişe ekler ve turun state çıkarımını
    /// tetikler.
    /// </summary>
    /// <param name="signals">
    /// Bu tur için LLM reasoning'inin ürettiği intent/sentiment sinyalleri (opsiyonel).
    /// Verilirse kural tabanlı çıkarımın yerine geçer — bkz. <see cref="TurnSignals"/>.
    /// <c>null</c> geçmek "LLM sinyali yok, kural tabanlı çıkarımı kullan" demektir;
    /// bu alanları tur ortasında AYRICA yazmak çift sayıma yol açar.
    /// </param>
    Task AddExchangeAsync(string sessionId, string userMessage, string botResponse,
        TurnSignals? signals = null, CancellationToken ct = default);
    Task AppendAssistantMessageAsync(string sessionId, string text, CancellationToken ct = default);
    Task AppendUserMessageAsync(string sessionId, string text, CancellationToken ct = default);
    Task ClearSessionAsync(string sessionId, CancellationToken ct = default);
    /// <summary>
    /// Oturum listesi. <paramref name="forCustomerId"/> verilirse YALNIZCA o müşteriye bağlı
    /// oturumlar döner; null ise hepsi (admin/agent panelleri için).
    ///
    /// <para>
    /// Filtre burada, veri kaynağında uygulanır — çağıranın listeyi aldıktan sonra ayıklamasına
    /// bırakılmaz. Uç noktanın filtrelemeyi unutması, bir müşterinin diğer tüm müşterilerin
    /// oturum kimliklerini görmesi demektir.
    /// </para>
    /// </summary>
    Task<List<SessionInfo>> GetAllSessionsAsync(string? forCustomerId = null, CancellationToken ct = default);

    // ─── State extraction ───

    Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct = default);
}
