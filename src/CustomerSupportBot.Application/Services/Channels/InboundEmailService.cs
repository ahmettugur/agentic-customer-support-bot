// Application/Services/Channels/InboundEmailService.cs
// Kanal sürekliliği: müşterinin e-posta yanıtı AYNI konuşmaya düşer, bot yanıtı kayıtlı adrese e-postayla döner.

using System.Collections.Concurrent;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Notifications;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Channels;

public sealed class InboundEmailOptions
{
    public const string SectionName = "InboundEmail";

    /// <summary>Gelen e-posta webhook'u; KAPALI başlar (kimlik doğrulaması sağlayıcıya bağlıdır).</summary>
    public bool Enabled { get; set; }

    /// <summary>Webhook çağrısını doğrulayan paylaşılan gizli (<c>X-Inbound-Secret</c>). Boşken uç hiçbir isteği kabul etmez.</summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Gönderenin SPF/DKIM'den geçtiğini sağlayıcı bildirmeli. "From" başlığı sahte olabilir; bu bayrak olmadan e-posta
    /// bir müşteri kimliği sayılmaz.
    /// </summary>
    public bool RequireAuthenticatedSender { get; set; } = true;

    /// <summary>Son etkinliği bu kadar günden yeni oturum sürdürülür; yoksa yeni konuşma açılır.</summary>
    public int SessionContinuityDays { get; set; } = 7;

    /// <summary>Bir göndericiden saatte en çok bu kadar e-posta işlenir (pod başına; LLM maliyeti ve kötüye kullanım koruması).</summary>
    public int MaxPerSenderPerHour { get; set; } = 10;

    /// <summary>Bir e-postadan alınacak en çok fotoğraf (sohbetteki mesaj başına sınırla aynı mantık).</summary>
    public int MaxAttachments { get; set; } = 3;
}

/// <param name="Authenticated">Sağlayıcının SPF/DKIM doğrulama sonucu.</param>
/// <param name="AutoSubmitted"><c>Auto-Submitted</c> başlığı (varsa).</param>
public sealed record InboundEmail(
    string From, string? Subject, string? Text, string? MessageId, bool Authenticated, string? AutoSubmitted,
    IReadOnlyList<InboundAttachment>? Attachments = null);

public sealed record InboundAttachment(string Name, string ContentType, byte[] Data);

public enum InboundEmailOutcome
{
    Replied, Disabled, Unauthenticated, Automated, UnknownSender, Duplicate, Empty, Rejected, Throttled
}

public sealed class InboundEmailService(
    ICustomerRepository customers,
    ISessionManager sessions,
    IChatPort chat,
    IInputGuard inputGuard,
    INotificationLedger ledger,
    IEmailSender sender,
    IOptions<InboundEmailOptions> options,
    IOptions<EmailOptions> email,
    ILogger<InboundEmailService> logger,
    TimeProvider? clock = null,
    InboundEmailThrottle? throttle = null,
    IChatAttachmentPort? attachments = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly InboundEmailThrottle _throttle = throttle ?? new InboundEmailThrottle();

    public async Task<InboundEmailOutcome> HandleAsync(InboundEmail mail, CancellationToken ct = default)
    {
        var o = options.Value;
        if (!o.Enabled) return InboundEmailOutcome.Disabled;

        var from = (mail.From ?? "").Trim();
        if (o.RequireAuthenticatedSender && !mail.Authenticated) return Log(InboundEmailOutcome.Unauthenticated, from);

        // Kendi adresimizden gelen posta da otomatik sayılır: aksi halde bot kendi bildirimine yanıt verip döngüye girer.
        if (EmailReplyExtractor.IsAutomated(from, mail.Subject, mail.AutoSubmitted)
            || string.Equals(from, email.Value.FromAddress, StringComparison.OrdinalIgnoreCase))
            return Log(InboundEmailOutcome.Automated, from);

        var customerId = await customers.FindIdByEmailAsync(from, ct);
        if (customerId is null) return Log(InboundEmailOutcome.UnknownSender, from);

        var text = EmailReplyExtractor.ExtractNewText(mail.Text, InputGuard.MaxInputLength);
        var photos = ImagesOf(mail, o);
        // Yalnızca fotoğraflı e-posta geçerli: metni boşsa kısa bir not konur (sohbetteki fotoğraf-only mesajla aynı).
        if (text.Length == 0 && (photos.Count == 0 || attachments is null)) return Log(InboundEmailOutcome.Empty, from);
        if (text.Length == 0) text = "Fotoğraf ekledim.";

        if (!AllowSender(from, o.MaxPerSenderPerHour)) return Log(InboundEmailOutcome.Throttled, from);

        var guard = inputGuard.Inspect(text);
        if (guard.Verdict == InputGuardVerdict.Reject) return Log(InboundEmailOutcome.Rejected, from);

        // Aynı posta sağlayıcıdan iki kez gelebilir (yeniden deneme): tur bir kez çalışır.
        var key = string.IsNullOrWhiteSpace(mail.MessageId) ? null : $"inbound-email:{mail.MessageId.Trim()}";
        if (key is not null && !await ledger.TryClaimAsync(key, ct)) return InboundEmailOutcome.Duplicate;

        try
        {
            var customer = customerId.Value.ToString();
            var sessionId = await ContinuedSessionAsync(customer, o, ct);
            var (attachmentIds, uploadSession) = await UploadPhotosAsync(photos, sessionId, customer, ct);
            var response = await chat.HandleAsync(
                new ChatRequest(guard.SanitizedInput, uploadSession ?? sessionId, CustomerId: customer,
                    AttachmentIds: attachmentIds.Count == 0 ? null : attachmentIds), ct);

            // Yanıt, e-postada yazanın değil KAYITLI adrese gider.
            var to = await customers.GetEmailAsync(customerId.Value, ct);
            if (string.IsNullOrWhiteSpace(to) || !sender.IsEnabled) return InboundEmailOutcome.Replied;

            var name = await customers.GetFullNameAsync(customerId.Value, ct);
            await sender.SendAsync(Compose(to, name, mail.Subject, response.Response, email.Value.PublicBaseUrl), ct);
            logger.LogInformation("[InboundEmail] Yanıtlandı | from={From} session={Session}", PiiMasker.MaskEmail(from), response.SessionId);
            return InboundEmailOutcome.Replied;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hata → sağlayıcı yeniden dener; talep geri bırakılmazsa yeniden deneme "Duplicate" sayılıp kaybolurdu.
            logger.LogError(ex, "[InboundEmail] Tur başarısız | from={From}", PiiMasker.MaskEmail(from));
            if (key is not null)
            {
                try { await ledger.ReleaseAsync(key, CancellationToken.None); }
                catch (Exception releaseEx) { logger.LogWarning(releaseEx, "[InboundEmail] Defter kaydı geri bırakılamadı | key={Key}", key); }
            }
            throw;
        }
    }

    private static List<InboundAttachment> ImagesOf(InboundEmail mail, InboundEmailOptions o) =>
        (mail.Attachments ?? []).Where(a => a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && a.Data.Length > 0)
            .Take(Math.Max(0, o.MaxAttachments)).ToList();

    /// <summary>
    /// Fotoğraflar sohbetteki yüklemeyle AYNI porttan geçer (doğrulama, metadata temizleme, görsel analiz, sahiplik). İlk yükleme yeni oturum
    /// açabilir; sonrakiler ve tur o oturumu kullanır. Reddedilen/hatalı fotoğraf metin turunu engellemez.
    /// </summary>
    private async Task<(List<string> Ids, string? SessionId)> UploadPhotosAsync(
        List<InboundAttachment> photos, string? sessionId, string customerId, CancellationToken ct)
    {
        var ids = new List<string>();
        if (attachments is null) return (ids, null);

        string? current = sessionId;
        foreach (var p in photos)
        {
            try
            {
                var r = await attachments.UploadAsync(current, customerId, p.Data, ct);
                if (r.Status != AttachmentUploadStatus.Ok || r.AttachmentId is null)
                {
                    logger.LogInformation("[InboundEmail] Fotoğraf reddedildi ({Status})", r.Status);
                    continue;
                }
                ids.Add(r.AttachmentId);
                current = r.SessionId ?? current;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[InboundEmail] Fotoğraf işlenemedi");
            }
        }
        return (ids, ids.Count > 0 ? current : null);
    }

    private async Task<string?> ContinuedSessionAsync(string customerId, InboundEmailOptions o, CancellationToken ct)
    {
        var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, o.SessionContinuityDays));
        var all = await sessions.GetAllSessionsAsync(customerId, ct);
        return all.Where(s => s.LastActivity >= cutoff).OrderByDescending(s => s.LastActivity).FirstOrDefault()?.SessionId;
    }

    private bool AllowSender(string from, int perHour) => _throttle.TryAcquire(from, perHour, _clock.GetUtcNow().UtcDateTime);

    private InboundEmailOutcome Log(InboundEmailOutcome outcome, string from)
    {
        logger.LogInformation("[InboundEmail] Atlandı ({Outcome}) | from={From}", outcome, PiiMasker.MaskEmail(from));
        return outcome;
    }

    private static EmailMessage Compose(string to, string? name, string? subject, string answer, string? publicBaseUrl)
    {
        // Başlık enjeksiyonu: konu satırında satır sonu olamaz.
        var clean = new string((subject ?? "").Where(c => c is not ('\r' or '\n')).ToArray()).Trim();
        if (clean.Length > 150) clean = clean[..150];
        var subjectLine = clean.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? clean : $"Re: {(clean.Length == 0 ? "Destek talebiniz" : clean)}";

        var link = string.IsNullOrWhiteSpace(publicBaseUrl) ? null : publicBaseUrl.TrimEnd('/') + "/";
        var text = answer + (link is null ? "" : $"\n\nSohbeti uygulamada sürdürmek için: {link}") + "\n\nBu yanıt otomatik oluşturulmuştur; yanıtlayarak konuşmayı sürdürebilirsiniz.";
        var html = $"<p>{System.Net.WebUtility.HtmlEncode(answer).Replace("\n", "<br>")}</p>"
                   + (link is null ? "" : $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(link)}\">Sohbeti uygulamada sürdürün</a></p>")
                   + "<p style=\"color:#64748b;font-size:12px\">Bu yanıt otomatik oluşturulmuştur; yanıtlayarak konuşmayı sürdürebilirsiniz.</p>";
        return new EmailMessage(to, name, subjectLine, text, html);
    }
}

/// <summary>
/// Gönderici başına kayan saatlik pencere. Durum burada tutulur (SINGLETON); <see cref="InboundEmailService"/> ise istek başına
/// (scoped) çözülür çünkü kapsamlı bağımlılıklara (sohbet portu) ihtiyaç duyar. Pod başınadır: çok pod'da gerçek sınır pod sayısıyla çarpılır.
/// </summary>
public sealed class InboundEmailThrottle
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string sender, int perHour, DateTime nowUtc)
    {
        var window = _windows.GetOrAdd(sender, _ => new Queue<DateTime>());
        lock (window)
        {
            while (window.Count > 0 && nowUtc - window.Peek() >= TimeSpan.FromHours(1)) window.Dequeue();
            if (window.Count >= Math.Max(1, perHour)) return false;
            window.Enqueue(nowUtc);
            return true;
        }
    }
}
