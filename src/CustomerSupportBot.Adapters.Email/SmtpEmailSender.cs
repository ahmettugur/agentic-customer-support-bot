// Adapters.Email/SmtpEmailSender.cs
// IEmailSender — SMTP (MailKit). Ayarlar appsettings "Email" bölümünden.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CustomerSupportBot.Adapters.Email;

/// <summary>
/// Her gönderimde bağlanır, gönderir, bağlantıyı kapatır. Bildirimler seyrek olduğu için kalıcı bağlantı
/// havuzu tutulmaz (SMTP sunucuları boşta bağlantıyı zaten düşürür). Hata çağırana fırlatılır: bildirim
/// servisi talebini geri bırakıp loglar.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public bool IsEnabled => true;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? "", message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();

        var smtp = _options.Smtp;
        using var client = new SmtpClient { Timeout = (int)TimeSpan.FromSeconds(Math.Max(1, smtp.TimeoutSeconds)).TotalMilliseconds };
        await client.ConnectAsync(smtp.Host, smtp.Port, ToSocketOptions(smtp.Security), ct);
        if (!string.IsNullOrEmpty(smtp.Username))
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? "", ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogDebug("[Email] SMTP gönderimi tamam | host={Host}", smtp.Host);
    }

    internal static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto
    };
}

/// <summary><c>Email:Enabled=false</c> iken kayıtlı — hiçbir şey göndermez.</summary>
public sealed class NullEmailSender(ILogger<NullEmailSender> logger) : IEmailSender
{
    public bool IsEnabled => false;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogDebug("[Email] Gönderim kapalı (Email:Enabled=false) — e-posta gönderilmedi");
        return Task.CompletedTask;
    }
}
