// Domain/Model/ConversationMessage.cs
// Saf domain konuşma mesajı — herhangi bir dış kütüphane tipine bağımlılık yok.
// Adaptörler kendi teknolojilerine (ChatMessage, LangChain, vb.) dönüşümü sınır noktasında yapar.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Konuşma geçmişindeki tek bir mesajı temsil eden domain tipi.
/// </summary>
public sealed record ConversationMessage(string Role, string Text);

/// <summary>
/// Konuşma rolü sabitleri.
/// </summary>
public static class ConversationRoles
{
    public const string System    = "system";
    public const string User      = "user";
    public const string Assistant = "assistant";
}
