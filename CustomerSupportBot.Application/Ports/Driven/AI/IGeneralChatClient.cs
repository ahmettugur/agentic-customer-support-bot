// Application/Ports/Driven/AI/IGeneralChatClient.cs
// SECONDARY PORT — Genel amaçlı LLM metin tamamlama.
// LessonMiner, CustomerProfileService, ConversationSummaryProvider gibi servisler
// doğrudan IChatClient yerine bu port'a bağımlıdır.
// Implementasyon: Adapters.AI katmanında GeneralChatClientAdapter.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Driven.AI;

/// <summary>
/// Genel LLM metin tamamlama için secondary (driven) port.
/// Framework'e özgü IChatClient'ı core'dan gizler.
/// </summary>
public interface IGeneralChatClient
{
    /// <summary>Verilen mesaj listesi ile LLM'den metin yanıtı alır.</summary>
    Task<string> CompleteAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken ct = default);
}
