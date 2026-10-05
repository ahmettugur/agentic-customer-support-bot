// Ports/Outbound/EmailOptions.cs
// E-posta gönderimi — appsettings "Email" bölümü. Tamamı yapılandırmadan; kapalıyken hiçbir şey gönderilmez.

using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Ports.Outbound;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; set; }

    /// <summary>Gönderen adresi — <see cref="Enabled"/> iken zorunlu.</summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "Müşteri Destek";

    /// <summary>E-postadaki "uygulamada görüntüle" bağlantısının kökü (ör. https://destek.example.com). Boşsa bağlantı konmaz.</summary>
    public string? PublicBaseUrl { get; set; }

    public SmtpOptions Smtp { get; set; } = new();

    public NotificationOptions Notifications { get; set; } = new();

    public sealed class SmtpOptions
    {
        public string? Host { get; set; }
        public int Port { get; set; } = 587;
        public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

        /// <summary>Boşsa kimlik doğrulama yapılmaz.</summary>
        public string? Username { get; set; }

        /// <summary>Repoya yazılmamalı — ortam değişkeni (<c>Email__Smtp__Password</c>) ya da secret ile verilir.</summary>
        public string? Password { get; set; }

        public int TimeoutSeconds { get; set; } = 30;
    }

    public sealed class NotificationOptions
    {
        /// <summary>Onay sonucu (onaylandı/reddedildi/zaman aşımı) müşteriye e-postayla bildirilir.</summary>
        public bool ApprovalResults { get; set; } = true;

        /// <summary>Sipariş kargoya verildiğinde (kargo firması ve takip numarasıyla) müşteriye e-posta.</summary>
        public bool OrderShipped { get; set; } = true;

        /// <summary>Sipariş teslim edildiğinde müşteriye e-posta.</summary>
        public bool OrderDelivered { get; set; } = true;
    }
}

public enum SmtpSecurity
{
    None,
    StartTls,
    SslOnConnect,
    Auto
}

/// <summary>
/// <c>Enabled=true</c> iken eksik ayar başlangıçta hata verir: sessizce gönderilmeyen e-posta, hiç
/// yapılandırılmamış e-postadan daha kötüdür — kimse fark etmez.
/// </summary>
public sealed class EmailOptionsValidator : IValidateOptions<EmailOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailOptions o)
    {
        if (!o.Enabled) return ValidateOptionsResult.Success;

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(o.FromAddress) || !o.FromAddress.Contains('@'))
            errors.Add("Email:FromAddress geçerli bir e-posta adresi olmalı.");
        if (string.IsNullOrWhiteSpace(o.Smtp.Host))
            errors.Add("Email:Smtp:Host zorunlu.");
        if (o.Smtp.Port is < 1 or > 65535)
            errors.Add("Email:Smtp:Port 1–65535 arasında olmalı.");
        if (o.Smtp.TimeoutSeconds < 1)
            errors.Add("Email:Smtp:TimeoutSeconds en az 1 olmalı.");
        // Yalnız http(s): Unix'te "/sohbet" gibi bir yol da "mutlak" sayılır (file:///sohbet).
        if (!string.IsNullOrWhiteSpace(o.PublicBaseUrl)
            && !(Uri.TryCreate(o.PublicBaseUrl, UriKind.Absolute, out var baseUrl)
                 && (baseUrl.Scheme == Uri.UriSchemeHttp || baseUrl.Scheme == Uri.UriSchemeHttps)))
            errors.Add("Email:PublicBaseUrl mutlak bir http(s) adresi olmalı (ör. https://destek.example.com).");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
