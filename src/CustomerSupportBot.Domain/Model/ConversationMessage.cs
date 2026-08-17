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
