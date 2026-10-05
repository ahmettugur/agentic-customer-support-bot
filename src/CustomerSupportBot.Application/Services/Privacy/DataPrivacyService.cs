// Application/Services/Privacy/DataPrivacyService.cs
// Kişisel veri (KVKK): saklama süresi temizliği, müşterinin verisini dışa aktarma ve silme.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Privacy;

/// <summary>
/// <see cref="IDataPrivacyPort"/> uygulaması.
///
/// <para>
/// <b>Oturum silme sırası:</b> önce oturuma bağlı veriyi tutan tüm depolar
/// (<see cref="ISessionDataEraser"/>), <b>en son</b> oturumun kendisi. Bir depo hata verirse oturum yerinde
/// kalır: saklama taraması onu bir sonraki turda yeniden bulur, silme talebi güvenle tekrarlanabilir.
/// Tersi sırada oturum silinmiş ama bağlı kayıtlar kalmış olurdu — ve artık hangi oturuma ait
/// olduklarını bulacak bir giriş noktası kalmazdı.
/// </para>
///
/// <para>
/// <b>Silinmeyenler:</b> sipariş, şikayet ve onay kayıtları yasal/işlemsel kayıttır; dışa aktarmada yer alır
/// ama silinmez. Hesap kapatma ayrı bir süreçtir.
/// </para>
/// </summary>
public sealed class DataPrivacyService : IDataPrivacyPort
{
    private readonly ISessionManager _sessions;
    private readonly IAttachmentStore _attachments;
    private readonly IReadOnlyList<ISessionDataEraser> _sessionErasers;
    private readonly IReadOnlyList<ICustomerDataEraser> _customerErasers;
    private readonly ICustomerProfileStore _profiles;
    private readonly IRatingStore _ratings;
    private readonly IApprovalQueue _approvals;
    private readonly IOrderRepository _orders;
    private readonly IComplaintRepository _complaints;
    private readonly DataRetentionOptions _options;
    private readonly ILogger<DataPrivacyService> _logger;
    private readonly IConversationDispositionStore? _dispositions;

    /// <summary>Onay geçmişinden dışa aktarılacak en fazla kayıt.</summary>
    private const int MaxExportedApprovals = 1000;

    public DataPrivacyService(
        ISessionManager sessions,
        IAttachmentStore attachments,
        IEnumerable<ISessionDataEraser> sessionErasers,
        IEnumerable<ICustomerDataEraser> customerErasers,
        ICustomerProfileStore profiles,
        IRatingStore ratings,
        IApprovalQueue approvals,
        IOrderRepository orders,
        IComplaintRepository complaints,
        IOptions<DataRetentionOptions> options,
        ILogger<DataPrivacyService> logger,
        IConversationDispositionStore? dispositions = null)
    {
        _dispositions = dispositions;
        _sessions = sessions;
        _attachments = attachments;
        _sessionErasers = sessionErasers.ToList();
        _customerErasers = customerErasers.ToList();
        _profiles = profiles;
        _ratings = ratings;
        _approvals = approvals;
        _orders = orders;
        _complaints = complaints;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RetentionResult> RunRetentionAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        if (!_options.Enabled) return new RetentionResult(false, 0, 0, []);

        var failures = new List<string>();

        // 0 gün = o tür için kapalı.
        var attachmentsDeleted = 0;
        if (_options.AttachmentRetentionDays > 0)
        {
            try
            {
                attachmentsDeleted = await _attachments.DeleteCreatedBeforeAsync(
                    nowUtc.AddDays(-_options.AttachmentRetentionDays), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[Privacy] Süresi dolan fotoğraflar silinemedi");
                failures.Add("attachments: " + ex.Message);
            }
        }

        var sessionsErased = 0;
        if (_options.ConversationRetentionDays > 0)
        {
            var ids = await _sessions.GetInactiveSessionIdsAsync(
                nowUtc.AddDays(-_options.ConversationRetentionDays), Math.Max(1, _options.MaxSessionsPerSweep), ct);
            if (ids.Count > 0)
            {
                var outcome = await EraseSessionsAsync(ids, ct);
                sessionsErased = outcome.SessionsErased;
                failures.AddRange(outcome.Failures.Select(f => $"{f.Store}: {f.Error.Message}"));
            }
        }

        if (sessionsErased > 0 || attachmentsDeleted > 0 || failures.Count > 0)
        {
            _logger.LogInformation(
                "[Privacy] Saklama taraması: {Sessions} oturum, {Attachments} fotoğraf silindi, {Failures} hata",
                sessionsErased, attachmentsDeleted, failures.Count);
        }

        return new RetentionResult(true, sessionsErased, attachmentsDeleted, failures);
    }

    public async Task<CustomerDataExport> ExportCustomerDataAsync(string customerId, CancellationToken ct = default)
    {
        var sessions = new List<ExportedSession>();
        var ratings = new List<Domain.Model.ConversationRating>();

        foreach (var info in await _sessions.GetAllSessionsAsync(customerId, ct))
        {
            var session = await _sessions.GetAsync(info.SessionId, ct);
            if (session is null) continue;

            var history = await _sessions.GetHistoryAsync(info.SessionId, ct);
            var attachments = (await _attachments.ListForSessionAsync(info.SessionId, ct))
                .Where(a => string.Equals(a.CustomerId, customerId, StringComparison.Ordinal))
                .Select(a => new ExportedAttachment(a.Id, a.ContentType, a.Description, a.CreatedAt, a.SentAt))
                .ToList();

            var dispositions = _dispositions is null
                ? null
                : (await _dispositions.ListForSessionAsync(info.SessionId, ct))
                    .Select(d => new ExportedDisposition(d.ReasonCode, d.Tags, d.Note, d.ClosedAt))
                    .ToList();

            sessions.Add(new ExportedSession(session.SessionId, session.CreatedAt, session.LastActivity, history, attachments, dispositions));
            if (_ratings.GetBySession(info.SessionId) is { } rating) ratings.Add(rating);
        }

        var approvals = (await _approvals.GetHistoryForCustomerAsync(customerId, MaxExportedApprovals, ct))
            .Select(a => new ExportedApproval(a.Id, a.ToolName, a.Status.ToString(), a.RequestedAt, a.DecidedAt, a.DecisionReason))
            .ToList();
        var orders = _orders.GetByCustomer(customerId)
            .Select(o => new ExportedOrder(o.OrderId, o.Order.Status, o.Order.OrderDate, o.Order.LinesSummary()))
            .ToList();
        var complaints = _complaints.GetByCustomer(customerId)
            .Select(c => new ExportedComplaint(c.ComplaintId, c.Complaint.OrderId, c.Complaint.Complaint, c.Complaint.Status))
            .ToList();

        return new CustomerDataExport(
            customerId,
            DateTime.UtcNow,
            _profiles.Get(customerId),
            sessions.OrderBy(s => s.CreatedAt).ToList(),
            ratings,
            approvals,
            orders,
            complaints);
    }

    public async Task<ErasureResult> EraseCustomerDataAsync(string customerId, CancellationToken ct = default)
    {
        var sessionIds = (await _sessions.GetAllSessionsAsync(customerId, ct)).Select(s => s.SessionId).ToList();
        var outcome = await EraseSessionsAsync(sessionIds, ct);
        var failures = outcome.Failures.ToList();
        var records = new Dictionary<string, int>(outcome.RecordsByStore);

        foreach (var eraser in _customerErasers)
        {
            try
            {
                var n = await eraser.EraseCustomerAsync(customerId, ct);
                records[eraser.Name] = records.GetValueOrDefault(eraser.Name) + n;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add((eraser.Name, ex));
            }
        }

        try
        {
            if (await _profiles.DeleteAsync(customerId)) records["profile"] = 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add(("profile", ex));
        }

        if (failures.Count > 0)
        {
            foreach (var (store, error) in failures)
                _logger.LogError(error, "[Privacy] Müşteri verisi silinemedi | store={Store}", store);
            throw new DataErasureException(failures.Select(f => f.Store).Distinct().ToList(), failures[0].Error);
        }

        // Müşteri kimliği loglanır (talebin denetim izi), silinen içerik loglanmaz.
        _logger.LogInformation(
            "[Privacy] Müşteri verisi silindi | customer={CustomerId} sessions={Sessions}",
            customerId, outcome.SessionsErased);
        return new ErasureResult(customerId, outcome.SessionsErased, records);
    }

    private sealed record SessionErasureOutcome(
        int SessionsErased,
        IReadOnlyDictionary<string, int> RecordsByStore,
        IReadOnlyList<(string Store, Exception Error)> Failures);

    /// <summary>Oturumlara bağlı veriyi siler, hepsi başarılıysa oturumları siler (sıra için sınıf notuna bakın).</summary>
    private async Task<SessionErasureOutcome> EraseSessionsAsync(IReadOnlyList<string> sessionIds, CancellationToken ct)
    {
        var records = new Dictionary<string, int>();
        var failures = new List<(string, Exception)>();
        if (sessionIds.Count == 0) return new SessionErasureOutcome(0, records, failures);

        foreach (var eraser in _sessionErasers)
        {
            try
            {
                records[eraser.Name] = records.GetValueOrDefault(eraser.Name) + await eraser.EraseSessionsAsync(sessionIds, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[Privacy] Oturum verisi silinemedi | store={Store} sessions={Count}",
                    eraser.Name, sessionIds.Count);
                failures.Add((eraser.Name, ex));
            }
        }

        if (failures.Count > 0) return new SessionErasureOutcome(0, records, failures);

        var erased = 0;
        foreach (var id in sessionIds)
        {
            try
            {
                await _sessions.ClearSessionAsync(id, ct);
                erased++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(("sessions", ex));
            }
        }
        records["sessions"] = erased;
        return new SessionErasureOutcome(erased, records, failures);
    }
}
