# CustomerSupportBot.Adapters.Email

`IEmailSender` portunun SMTP adaptörü (MailKit). Tasarım:
[docs/superpowers/specs/2026-10-05-email-notifications-design.md](../superpowers/specs/2026-10-05-email-notifications-design.md).

## Dosyalar

- `SmtpEmailSender` — her gönderimde bağlanır, (kullanıcı adı verilmişse) kimlik doğrular, gönderir, kapatır.
  Düz metin + HTML (multipart/alternative). Hata çağırana fırlatılır; bildirim servisi talebi geri bırakır.
- `NullEmailSender` — `Email:Enabled=false` iken kayıtlı; hiçbir şey göndermez.
- `DependencyInjection/EmailAdapterServiceCollectionExtensions.AddEmailAdapter()` — ayara göre birini kaydeder.

## Yapılandırma (`Email`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `Enabled` | `false` | Tüm e-posta gönderimi. |
| `FromAddress` / `FromName` | `destek@example.com` / `Müşteri Destek` | Gönderen. Açıkken `FromAddress` zorunlu. |
| `PublicBaseUrl` | boş | E-postadaki bağlantının kökü (yalnız http/https); boşsa bağlantı konmaz. |
| `Smtp:Host` / `Smtp:Port` | boş / `587` | Açıkken `Host` zorunlu. |
| `Smtp:Security` | `StartTls` | `None`, `StartTls`, `SslOnConnect` (genelde 465), `Auto`. |
| `Smtp:Username` / `Smtp:Password` | boş | Boşsa kimlik doğrulama yok. **Parola repoya yazılmaz**: `Email__Smtp__Password` ortam değişkeni ya da secret. |
| `Smtp:TimeoutSeconds` | `30` | |
| `Notifications:ApprovalResults` | `true` | Onay sonucu e-postası. |

Açıkken eksik/geçersiz ayar uygulama başlangıcında `OptionsValidationException` verir (`EmailOptionsValidator`).

## Test

`SmtpEmailSenderTests` gerçek bir SMTP test sunucusuna (Mailpit, Testcontainers) gönderir ve e-postayı
Mailpit'in API'sinden geri okur: alıcı, gönderen, düz metin ve HTML gövde.
