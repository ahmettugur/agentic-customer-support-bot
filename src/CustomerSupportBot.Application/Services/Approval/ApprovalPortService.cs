// Application/Services/ApprovalPortService.cs
// DRIVING PORT IMPL — IApprovalPort → HITL onay kuyruğu orkestrasyonu.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Approval;

/// <summary>
/// HITL onay akışı driving port implementasyonu.
/// Admin panel (AgentPanelEndpoints) bu sınıfı IApprovalPort olarak kullanır.
/// </summary>
public sealed class ApprovalPortService : IApprovalPort
{
    private readonly IApprovalQueue _approvalQueue;
    private readonly ICustomerRepository _customers;
    private readonly ILogger<ApprovalPortService> _logger;

    public ApprovalPortService(
        IApprovalQueue approvalQueue,
        ICustomerRepository customers,
        ILogger<ApprovalPortService> logger)
    {
        _approvalQueue = approvalQueue;
        _customers = customers;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)
    {
        // Kalıcı okuma: admin panelinde görünmeyen bir talep hiç işlenmez.
        var pending = await _approvalQueue.GetPendingAsync(ct);
        await EnrichCustomerNamesAsync(pending, ct);
        return pending;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> GetRecentAsync(int count = 50, CancellationToken ct = default)
    {
        var recent = _approvalQueue.GetRecent(count);
        await EnrichCustomerNamesAsync(recent, ct);
        return recent;
    }

    public async Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default)
    {
        var stuck = await _approvalQueue.GetStuckExecutionsAsync(ct);
        await EnrichCustomerNamesAsync(stuck, ct);
        return stuck;
    }

    /// <summary>
    /// Onay kayıtlarına müşteri adını yazar — admin "Müşteri #1027"yi değil, kimin adına karar
    /// verdiğini görsün diye.
    ///
    /// <para>
    /// Ad kalıcı DEĞİLDİR (bkz. <see cref="ApprovalRequest.CustomerName"/>); burada, panele
    /// gönderilmeden hemen önce doldurulur. Tek bir toplu sorgu kullanılır — kart başına ayrı
    /// sorgu, kuyruk büyüdükçe panelin açılışını doğrusal olarak yavaşlatırdı.
    /// </para>
    ///
    /// <para>
    /// <b>Not:</b> <c>IApprovalQueue</c> cache'teki canlı nesneleri döndürür, yani bu yazma
    /// paylaşılan örnekleri değiştirir. Kasıtlı ve zararsız: değer aynı müşteri için hep aynıdır,
    /// kalıcılık eşlemesinde yer almaz (yazılmaz), ve ikinci çağrıda tekrar sorgulanmasını
    /// engelleyerek fiilen memoizasyon görevi görür. Adı değişen müşteri, kayıt cache'ten
    /// düştüğünde ya da uygulama yeniden başladığında güncellenir.
    /// </para>
    ///
    /// <para>
    /// Ad çözülemezse (silinmiş müşteri, sayısal olmayan kimlik) alan <c>null</c> kalır ve panel
    /// yalnızca numarayı gösterir — bu yüzden hata durumu istisna değil, sessiz bir düşüştür.
    /// </para>
    /// </summary>
    private async Task EnrichCustomerNamesAsync(IReadOnlyList<ApprovalRequest> requests, CancellationToken ct)
    {
        var missing = requests
            .Where(r => r.CustomerName is null && long.TryParse(r.CustomerId, out _))
            .Select(r => long.Parse(r.CustomerId!))
            .ToList();

        if (missing.Count == 0) return;

        try
        {
            var names = await _customers.GetFullNamesAsync(missing, ct);
            foreach (var request in requests)
            {
                if (request.CustomerName is not null) continue;
                if (long.TryParse(request.CustomerId, out var id) && names.TryGetValue(id, out var name))
                    request.CustomerName = name;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ad yalnızca bir görüntüleme kolaylığı — çözülemezse onay kuyruğu yine de
            // açılmalı. Kart numaraya düşer.
            _logger.LogWarning(ex, "Onay kuyruğu için müşteri adları çözülemedi.");
        }
    }

    public ApprovalRequest? Get(string id)
    {
        return _approvalQueue.Get(id);
    }

    public async Task<bool> DecideAsync(string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default)
    {
        var request = await _approvalQueue.GetAsync(id, ct);
        if (request is null)
        {
            _logger.LogWarning("Approval request not found: {Id}", id);
            return false;
        }

        if (request.Status != ApprovalStatus.Pending)
        {
            _logger.LogDebug("Approval request already decided: {Id} status={Status}", id, request.Status);
            return false;
        }

        var result = await _approvalQueue.DecideAsync(id, approved, decidedBy, reason, ct);

        if (result)
        {
            _logger.LogInformation(
                "Approval decision applied: {Id} approved={Approved} by={DecidedBy}",
                id, approved, decidedBy ?? "unknown");
        }

        return result;
    }

    public Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct = default) =>
        _approvalQueue.GetAsync(id, ct);
}
