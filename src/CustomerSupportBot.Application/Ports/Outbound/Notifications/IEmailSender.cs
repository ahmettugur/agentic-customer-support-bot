// Ports/Outbound/Notifications/IEmailSender.cs
// E-posta gönderimi — adaptör: CustomerSupportBot.Adapters.Email (SMTP).

namespace CustomerSupportBot.Application.Ports.Outbound.Notifications;

/// <param name="HtmlBody">İsteğe bağlı HTML sürümü; içine giren kullanıcı metinleri çağıran tarafından escape edilmiş olmalı.</param>
public sealed record EmailMessage(string To, string? ToName, string Subject, string TextBody, string? HtmlBody = null);

public interface IEmailSender
{
    /// <summary>Gönderim açık mı (<c>Email:Enabled</c>)? Kapalıyken <see cref="SendAsync"/> hiçbir şey yapmaz.</summary>
    bool IsEnabled { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
