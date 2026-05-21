// Ports/Driven/Messaging/IMessageBusPort.cs
// SECONDARY PORT — Dağıtık mesajlaşma (pub/sub) soyutlaması.

namespace CustomerSupportBot.Application.Ports.Driven.Messaging;

/// <summary>
/// Yatay ölçeklendirme için pod'lar arası pub/sub mesaj yolu.
/// Persistence adaptörleri bu port üzerinden diğer pod'lara bildirim yapar.
/// </summary>
public interface IMessageBusPort
{
    /// <summary>Belirtilen kanala JSON payload yayınlar.</summary>
    void Publish(string channel, string jsonPayload);

    /// <summary>Belirtilen kanalı dinler; mesaj geldiğinde handler çağrılır.</summary>
    void Subscribe(string channel, Action<string> handler);

    /// <summary>Bu node'un benzersiz kimliği. Kendi mesajlarını filtrelemek için kullanılır.</summary>
    string NodeId { get; }
}
