namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>Onay ve eskalasyon kartlarındaki konuşma özeti satırı.</summary>
/// <param name="Speaker"><c>customer</c> ya da <c>assistant</c>.</param>
public sealed record ConversationExcerptLine(string Speaker, string Text);
