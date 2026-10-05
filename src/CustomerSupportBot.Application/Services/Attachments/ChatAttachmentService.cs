// Application/Services/Attachments/ChatAttachmentService.cs
// Sohbete fotoğraf ekleme: doğrulama → meta veri temizliği → görsel açıklama → kayıt.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Attachments;

/// <summary>
/// <see cref="IChatAttachmentPort"/> uygulaması.
///
/// <para>
/// <b>Tür</b> dosya imzasından belirlenir (<see cref="ImageSanitizer"/>); meta veri (EXIF konumu dahil)
/// <b>saklamadan ve görsel modele göndermeden önce</b> silinir. Görsel modelin açıklaması, kullanıcının
/// yazdığı metinle aynı <see cref="IInputGuard"/>'dan geçer: kişisel veri maskelenir; enjeksiyon olarak
/// reddedilirse açıklama kullanılmaz. Model hatası yüklemeyi bozmaz — fotoğraf açıklamasız kaydedilir.
/// </para>
///
/// <para>
/// <b>Oturum:</b> ilk mesajdan önce fotoğraf eklenebildiği için oturum yoksa burada açılır ve müşteriye
/// bağlanır. Bağlama, yazılı sohbet turuyla aynı kilit anahtarı altında yapılır.
/// </para>
/// </summary>
public sealed class ChatAttachmentService : IChatAttachmentPort
{
    private readonly IAttachmentStore _store;
    private readonly ISessionManager _sessions;
    private readonly IAppDistributedLock _locks;
    private readonly IImageAnalysisPort _analyzer;
    private readonly IInputGuard _guard;
    private readonly AttachmentOptions _options;
    private readonly ILogger<ChatAttachmentService> _logger;
    private readonly Ports.Outbound.Observability.ILlmCallAttribution? _attribution;

    public ChatAttachmentService(
        IAttachmentStore store,
        ISessionManager sessions,
        IAppDistributedLock locks,
        IImageAnalysisPort analyzer,
        IInputGuard guard,
        IOptions<AttachmentOptions> options,
        ILogger<ChatAttachmentService> logger,
        Ports.Outbound.Observability.ILlmCallAttribution? attribution = null)
    {
        _attribution = attribution;
        _store = store;
        _sessions = sessions;
        _locks = locks;
        _analyzer = analyzer;
        _guard = guard;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AttachmentUploadResult> UploadAsync(
        string? sessionId, string? authenticatedCustomerId, byte[] data, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return new(AttachmentUploadStatus.Disabled, Error: "Fotoğraf ekleme şu anda kapalı.");
        if (data.Length == 0)
            return new(AttachmentUploadStatus.Empty, Error: "Dosya boş.");
        if (data.Length > _options.MaxBytes)
            return new(AttachmentUploadStatus.TooLarge,
                Error: $"Fotoğraf en fazla {_options.MaxBytes / (1024 * 1024)} MB olabilir.");

        var kind = ImageSanitizer.Detect(data);
        var clean = kind == ImageKind.Unknown ? null : ImageSanitizer.StripMetadata(data, kind);
        if (clean is null)
            return new(AttachmentUploadStatus.UnsupportedType, Error: "Yalnızca JPEG veya PNG fotoğraf eklenebilir.");

        if (sessionId is not null && !SessionIdPolicy.IsValid(sessionId))
            return new(AttachmentUploadStatus.InvalidSession, Error: SessionIdPolicy.ErrorMessage);

        var sid = sessionId ?? Guid.NewGuid().ToString("N");
        if (!await EnsureSessionAsync(sid, authenticatedCustomerId, ct))
            return new(AttachmentUploadStatus.Forbidden, Error: "Bu oturuma erişim yetkiniz yok.");

        var existing = await _store.ListForSessionAsync(sid, ct);
        if (existing.Count >= _options.MaxPerSession)
            return new(AttachmentUploadStatus.TooManyInSession,
                Error: $"Bir sohbete en fazla {_options.MaxPerSession} fotoğraf eklenebilir.");

        var contentType = kind == ImageKind.Jpeg ? "image/jpeg" : "image/png";
        string? description;
        // Görüşme başına maliyet: görsel analiz bu görüşmeye atfedilir.
        using (_attribution?.BeginSession(sid)) description = await DescribeAsync(clean, contentType, ct);

        var attachment = new ChatAttachment
        {
            SessionId = sid,
            CustomerId = authenticatedCustomerId,
            ContentType = contentType,
            Data = clean,
            Description = description
        };
        await _store.SaveAsync(attachment, ct);

        _logger.LogInformation(
            "Attachment kaydedildi id={Id} session={Sid} type={Type} bytes={Bytes} described={Described}",
            attachment.Id, sid, contentType, clean.Length, description is not null);

        return new(AttachmentUploadStatus.Ok, attachment.Id, sid, description);
    }

    public async Task<ChatAttachment?> GetAsync(string id, string? customerId, CancellationToken ct = default)
    {
        var attachment = await _store.GetAsync(id, ct);
        if (attachment is null) return null;
        if (customerId is not null && !string.Equals(attachment.CustomerId, customerId, StringComparison.Ordinal))
            return null;
        return attachment;
    }

    public async Task<bool> DeleteUnsentAsync(string id, string customerId, CancellationToken ct = default)
    {
        var attachment = await _store.GetAsync(id, ct);
        if (attachment is null || !string.Equals(attachment.CustomerId, customerId, StringComparison.Ordinal))
            return false;
        return await _store.DeleteUnsentAsync(id, ct);
    }

    /// <summary>Oturumu getirir ya da açar ve müşteriye bağlar; başka müşteriye aitse <c>false</c>.</summary>
    private async Task<bool> EnsureSessionAsync(string sessionId, string? customerId, CancellationToken ct)
    {
        var current = await _sessions.GetAsync(sessionId, ct);
        if (current is not null && string.Equals(current.State.AuthenticatedCustomerId, customerId, StringComparison.Ordinal))
            return true;   // zaten bu müşterinin — kilit gerekmez (sürmekte olan bir turu bekletmeyiz)

        await using var handle = await _locks.AcquireAsync(SessionIdentityBinder.TurnLockKey(sessionId), ct: ct);
        var session = await _sessions.GetOrCreateAsync(sessionId, ct);
        return await SessionIdentityBinder.TryBindAsync(session, customerId, _sessions, ct);
    }

    private async Task<string?> DescribeAsync(byte[] image, string contentType, CancellationToken ct)
    {
        string raw;
        try
        {
            raw = await _analyzer.DescribeAsync(image, contentType, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Attachment: görsel analiz başarısız");
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw)) return null;

        var guard = _guard.Inspect(raw.Trim());
        if (guard.Verdict == InputGuardVerdict.Reject)
        {
            _logger.LogWarning("Attachment: görsel açıklama girdi filtresince reddedildi flags={Flags}",
                string.Join(",", guard.Flags));
            return null;
        }
        return guard.SanitizedInput;
    }
}
