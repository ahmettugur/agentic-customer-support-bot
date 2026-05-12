// Services/IChatModeRegistry.cs
// HITL Live Takeover — session başına (Bot|Human) kipini tutan registry sözleşmesi.
// ChatEndpoints her istekte GetMode() okur; AdminEndpoints TakeOver/Release yazar.

using CustomerSupportBot.Api.Models;

namespace CustomerSupportBot.Api.Services;

public interface IChatModeRegistry
{
    /// <summary>Session için mevcut kip. Bilinmeyen session → Bot.</summary>
    ChatMode GetMode(string sessionId);

    /// <summary>Full state snapshot (null ise session Bot modda, hiç kaydı yok).</summary>
    ChatSessionState? GetState(string sessionId);

    /// <summary>
    /// Admin bir session'ı devralır. humanAgent yoksa "admin" kullanılır.
    /// Halihazırda Human modaysa no-op (true döner).
    /// </summary>
    bool TakeOver(string sessionId, string? humanAgent);

    /// <summary>
    /// Admin Human modu sonlandırır; session Bot'a döner.
    /// Zaten Bot modaysa false döner.
    /// </summary>
    bool Release(string sessionId);

    /// <summary>Human modda olan tüm session'lar (admin UI için).</summary>
    IReadOnlyList<ChatSessionState> GetActive();

    /// <summary>
    /// Mod değiştiğinde fire edilir — ChatEndpoints SSE loop'u bu event'le
    /// Kendi bekleme state'ini bozar ve client'a human_joined/human_left yollar.
    /// </summary>
    event EventHandler<ChatSessionState>? ModeChanged;
}
