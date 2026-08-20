// Services/Persistence/PostgresApprovalQueue.cs
// HITL — Hibrit cache + PostgreSQL approval queue.
//
// Önemli: TaskCompletionSource süreç-içi senkronizasyon primitifidir; PERSIST EDİLMEZ.
// Bloklamayan onay modelinde (bkz. ApprovalGateService) zaten kimse onu beklemez: kaydı
// OLUŞTURAN pod dışındaki her pod'da (OnRemoteCreated / HydrateAsync) Tcs null'dır. Bu
// yüzden kararın yayılması, bildirimi ve okunması Tcs'e BAĞLI OLMAMALIDIR — bkz.
// OnRemoteDecided. Pending kayıtlar restart'ta artık expire EDİLMEZ (kimse beklemiyor);
// süresi geçenleri StaleApprovalSweepService periyodik olarak reddeder.
//
// Davranış (in-memory ile aynı):
//   - Create: DB'ye INSERT (Pending) + cache + TCS + RequestCreated event.
//   - AwaitDecisionAsync: TCS task'ını bekler. Timeout'ta otomatik karar.
//   - Decide: cache + TCS release + DB UPDATE + RequestDecided event.
//   - Lazy hydrate: tüm açık (Pending) + son N karar yüklenir.
//     (Hydrate edilen Pending'ler için yeni TCS oluşturulmaz — cache'e "sahipsiz"
//      kayıt olarak, okuma ve karar yayılımı için eklenirler.)
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Create: csbot:approval:created kanalına yayın → diğer pod'lar entry'yi cache'e ekler.
//   - Decide: DB'de koşullu sahiplenme (ClaimDecisionAsync, WHERE Status='Pending') →
//     yürütme → csbot:approval:decided kanalına yayın → diğer pod'lar cache'i günceller ve
//     RequestDecided'ı (SSE bildirimi) kendi bağlı istemcileri için tetikler.
//     Sahiplenme yürütmeden ÖNCE ve tek bir UPDATE ile yapılır: distributed lock yalnızca
//     eş zamanlı çağrıları serialize eder, bayat bir cache yüzünden SONRADAN gelen ikinci
//     bir kararın aynı işi tekrar yürütmesini engellemez — bunu DB koşulu engeller.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresApprovalQueue : IApprovalQueue
{
    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ApprovalOptions _options;
    private readonly ILogger<PostgresApprovalQueue> _logger;
    private readonly IMessageBusPort _messageBus;
    private readonly IAppDistributedLock _distributedLock;
    private readonly IApprovalExecutionRouter _executionRouter;
    private readonly ConcurrentDictionary<string, QueueEntry> _entries = new();
    private readonly object _hydrationLock = new();
    private volatile bool _hydrated;

    private const int HydrateRecentCount = 200;

    public event EventHandler<ApprovalRequest>? RequestCreated;
    public event EventHandler<ApprovalRequest>? RequestDecided;

    public PostgresApprovalQueue(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IOptions<ApprovalOptions> options,
        IMessageBusPort messageBus,
        IAppDistributedLock distributedLock,
        IApprovalExecutionRouter executionRouter,
        ILogger<PostgresApprovalQueue> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _distributedLock = distributedLock;
        _executionRouter = executionRouter;
        _messageBus = messageBus;
        _logger = logger;
        _messageBus.Subscribe("csbot:approval:created", OnRemoteCreated);
        _messageBus.Subscribe("csbot:approval:decided", OnRemoteDecided);
    }

    public async Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken ct = default)
    {
        EnsureHydrated();

        request.TimeoutSeconds = _options.TimeoutSeconds;
        var tcs = new TaskCompletionSource<ApprovalRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = new QueueEntry(request, tcs);
        _entries[request.Id] = entry;

        try { await InsertAsync(request, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Approval INSERT başarısız. Id={Id}", request.Id);
            _entries.TryRemove(request.Id, out _);
            throw ExceptionTranslator.Translate(ex, $"Approval oluşturulamadı: {request.Id}");
        }

        _logger.LogInformation(
            "[HITL] Approval request created: id={Id}, tool={Tool}, session={Session}",
            request.Id, request.ToolName, request.SessionId);

        try { RequestCreated?.Invoke(this, request); }
        catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler failed"); }

        PublishRedis("csbot:approval:created", new
        {
            nodeId = _messageBus.NodeId,
            id = request.Id,
            sessionId = request.SessionId,
            customerId = request.CustomerId,
            traceId = request.TraceId,
            toolName = request.ToolName,
            agentName = request.AgentName,
            parametersJson = JsonSerializer.Serialize(request.Parameters),
            userQuery = request.UserQuery,
            justification = request.Justification,
            requestedAt = request.RequestedAt,
            timeoutSeconds = request.TimeoutSeconds
        });

        return request;
    }

    public async Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default)
    {
        if (!_entries.TryGetValue(id, out var entry))
            throw new InvalidOperationException($"Approval request not found: {id}");

        // Hydrate edilmiş "orphan" kayıt — TCS gerçek bir bekleyene bağlı değil.
        if (entry.Tcs is null)
            throw new InvalidOperationException($"Approval request is orphan (no live waiter): {id}");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(entry.Request.TimeoutSeconds));

        // CancellationToken.Register yalnızca senkron Action kabul eder; asenkron
        // Decide/Update çağrılarını burada thread'i bloklamadan (fire-and-forget,
        // Task.Run ile) tetikliyoruz. entry.Tcs.Task zaten bu işin bitmesini bekler.
        using (cts.Token.Register(() => _ = HandleTimeoutAsync(id, entry)))
        {
            return await entry.Tcs.Task.ConfigureAwait(false);
        }
    }

    private async Task HandleTimeoutAsync(string id, QueueEntry entry)
    {
        if (entry.Tcs is null || entry.Tcs.Task.IsCompleted) return;

        var autoApprove = _options.AutoApproveOnTimeout;
        try
        {
            await DecideAsync(
                id,
                approved: autoApprove,
                decidedBy: WellKnown.Defaults.System,
                reason: autoApprove
                    ? WellKnown.ApprovalReasons.AutoApproveTimeout
                    : WellKnown.ApprovalReasons.TimeoutExpired).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HITL] Timeout auto-decide başarısız. Id={Id}", id);
        }

        if (!autoApprove)
        {
            entry.Request.Status = ApprovalStatus.Expired;
            try { await MarkExpiredIfPendingAsync(id).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Approval Expired UPDATE başarısız. Id={Id}", id);
            }
        }
    }

    public async Task<bool> DecideAsync(
        string id, bool approved, string? decidedBy = null, string? reason = null, CancellationToken ct = default)
    {
        EnsureHydrated();
        if (!_entries.TryGetValue(id, out var entry)) return false;

        // Distributed lock: farklı pod'lardan eş zamanlı DecideAsync() çağrılarını serialize eder.
        // TryAcquireAsync null dönerse (başka pod lock tutuyor) kararı reddet — double-decision önlemi.
        var handle = await _distributedLock.TryAcquireAsync($"approval:{id}", ct: ct).ConfigureAwait(false);
        if (handle is null)
        {
            _logger.LogWarning("[HITL] Approval distributed lock alınamadı; karar reddedildi. Id={Id}", id);
            return false;
        }

        try
        {
            // Ucuz ön eleme — bu pod zaten kararı biliyorsa DB'ye hiç gitme.
            if (entry.Request.Status != ApprovalStatus.Pending) return false;

            var decidedAt = DateTime.UtcNow;
            var newStatus = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            var resolvedBy = string.IsNullOrWhiteSpace(decidedBy) ? WellKnown.Defaults.Admin : decidedBy;

            // Onaylanan bir talebin yürütmesi kararla AYNI anda "Running" işaretlenir. Böylece
            // yürütme ile sonucun yazılması arasında süreç kapanırsa kayıt Approved+Running olarak
            // kalır ve askıda olduğu GÖRÜNÜR olur — bu pencere eskiden hiçbir yerde iz bırakmıyordu.
            var newExecStatus = approved ? ApprovalExecutionStatus.Running : ApprovalExecutionStatus.None;

            // ASIL KORUMA BURADA: kaydı DB'de koşullu olarak (WHERE Status='Pending') sahipleniriz.
            // Bellekteki Status tek başına yeterli DEĞİLDİR — bu pod kaydı oluşturmamışsa
            // durumu yalnızca Redis üzerinden öğrenir ve o mesaj kaybolabilir; o zaman bellek
            // "Pending" der, oysa başka bir pod çoktan onaylayıp iadeyi yürütmüştür. Distributed
            // lock yalnızca AYNI ANDA yürütmeyi engeller, sonradan tekrarı değil.
            //
            // Sahiplenme, yürütmeden ÖNCE yapılır: 0 satır etkilendiyse kararı başkası vermiştir,
            // gerçek iş (iade/iptal) hiç çalıştırılmaz.
            int claimed;
            try
            {
                claimed = await ClaimDecisionAsync(id, newStatus, newExecStatus, decidedAt, resolvedBy, reason, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Approval karar sahiplenme (UPDATE) başarısız. Id={Id}", id);
                throw;
            }

            if (claimed == 0)
            {
                // Karar başka bir pod'da verilmiş ve bizim belleğimiz bayat kalmış (kayıp Redis
                // mesajı veya biz oluşturmadığımız için Tcs'siz gelen kayıt). Belleği DB'den
                // tazeleyip kendimizi onarıyoruz ki sonraki okumalar (unseen/badge, sweep)
                // doğru durumu görsün.
                _logger.LogWarning(
                    "[HITL] Approval kararı başka bir pod'da verilmiş; yürütme atlandı. Id={Id}", id);
                await RefreshFromDbAsync(id, ct).ConfigureAwait(false);
                return false;
            }

            entry.Request.Status = newStatus;
            entry.Request.ExecutionStatus = newExecStatus;
            entry.Request.DecidedAt = decidedAt;
            entry.Request.DecidedBy = resolvedBy;
            entry.Request.DecisionReason = reason;

            // Onaylandıysa gerçek iş burada, karar anında tetiklenir — tool çağrısı artık
            // bunu beklemiyor (bkz. ApprovalGateService.ExecuteWithApprovalGateAsync).
            if (approved)
            {
                try
                {
                    var outcome = await _executionRouter.ExecuteAsync(entry.Request, ct).ConfigureAwait(false);
                    entry.Request.ExecutionResult = outcome.Message;
                    // outcome.Success eskiden atılıyordu: tool kendi işini reddetse bile kayıt
                    // yalnızca "Onaylandı" görünüyordu. İnsan kararı ile işin sonucu ayrı alanlar.
                    entry.Request.ExecutionStatus = outcome.Success
                        ? ApprovalExecutionStatus.Succeeded
                        : ApprovalExecutionStatus.Failed;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HITL] Approval execution başarısız. Id={Id}", id);
                    entry.Request.ExecutionResult = "İşlem yürütülürken bir hata oluştu.";
                    entry.Request.ExecutionStatus = ApprovalExecutionStatus.Failed;
                }
                entry.Request.ExecutedAt = DateTime.UtcNow;

                try { await WriteExecutionOutcomeAsync(entry.Request).ConfigureAwait(false); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[HITL] Approval sonuç UPDATE başarısız. Id={Id}", id);
                    throw;
                }
            }

            _logger.LogInformation(
                "[HITL] Approval decision: id={Id}, approved={Approved}, by={By}",
                id, approved, entry.Request.DecidedBy);

            try { RequestDecided?.Invoke(this, entry.Request); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler failed"); }

            entry.Tcs?.TrySetResult(entry.Request);

            PublishRedis("csbot:approval:decided", new
            {
                nodeId = _messageBus.NodeId,
                id,
                approved,
                decidedBy = entry.Request.DecidedBy,
                reason = entry.Request.DecisionReason,
                decidedAt = entry.Request.DecidedAt,
                executionResult = entry.Request.ExecutionResult,
                executedAt = entry.Request.ExecutedAt,
                executionStatus = entry.Request.ExecutionStatus.ToString()
            });

            return true;
        }
        finally
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
    }

    public IReadOnlyList<ApprovalRequest> GetPending()
    {
        EnsureHydrated();
        return _entries.Values
            .Where(e => e.Request.Status == ApprovalStatus.Pending)
            .Select(e => e.Request)
            .OrderBy(r => r.RequestedAt)
            .ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// DB'den okur ve okuduğunu cache'e de yansıtır: Redis mesajı kaybolduğu için buraya hiç
    /// ulaşmamış kayıtlar böylece bu pod'un cache'ine de girer ve bir daha kaybolmaz. Yani bu
    /// çağrı aynı zamanda cache ile DB arasındaki uzlaştırma (reconciliation) noktasıdır.
    /// Var olan girdiler EZİLMEZ — bu pod'un kendi TaskCompletionSource'u korunmalıdır.
    /// </remarks>
    public async Task<IReadOnlyList<ApprovalRequest>> GetPendingAsync(CancellationToken ct = default)
    {
        var pending = nameof(ApprovalStatus.Pending);

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var rows = await ctx.Approvals.AsNoTracking()
            .Where(a => a.Status == pending)
            .OrderBy(a => a.RequestedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var result = new List<ApprovalRequest>(rows.Count);
        foreach (var row in rows)
        {
            var domain = ToDomain(row);
            // Cache'te zaten varsa ONU döneriz: aynı kaydın iki farklı nesne örneğiyle
            // dolaşması, TCS'i olan girdinin yanında sahipsiz bir kopya bırakırdı.
            var entry = _entries.GetOrAdd(row.Id, _ => new QueueEntry(domain, tcs: null));
            result.Add(entry.Request.Status == ApprovalStatus.Pending ? entry.Request : domain);
        }

        return result;
    }

    public IReadOnlyList<ApprovalRequest> GetRecent(int count = 50)
    {
        EnsureHydrated();
        return _entries.Values
            .Select(e => e.Request)
            .OrderByDescending(r => r.RequestedAt)
            .Take(count)
            .ToList();
    }

    public ApprovalRequest? Get(string id)
    {
        EnsureHydrated();
        return _entries.TryGetValue(id, out var entry) ? entry.Request : null;
    }

    /// <summary>
    /// Doğrudan PostgreSQL'e sorar, cache'e DEĞİL.
    ///
    /// <para>
    /// Cache üzerinden okumak bu sorguyu Redis'in teslimatına bağımlı kılıyordu: <c>decided</c>
    /// (veya <c>created</c>) mesajını kaçırmış bir pod'da kayıt "Pending" görünür ya da hiç
    /// bulunmaz; buradaki <c>Status != Pending</c> filtresi de onu sessizce eler ve müşteri
    /// sonradan girdiğinde sonucu HİÇ göremezdi. Redis yayını en-fazla-bir-kez teslimattır ve
    /// hataları bilinçli olarak yutulur (bkz. <c>RedisMessageBusAdapter</c>) — dolayısıyla
    /// anlık bildirim için bir hızlandırma katmanıdır, kalıcı durumun kaynağı değildir.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<ApprovalRequest>> GetUnseenForSessionAsync(
        string sessionId, string customerId, CancellationToken ct = default)
    {
        var pending = nameof(ApprovalStatus.Pending);

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await ctx.Approvals.AsNoTracking()
            .Where(a => a.SessionId == sessionId
                     && a.CustomerId == customerId
                     && a.Status != pending && a.CustomerSeenAt == null)
            .OrderBy(a => a.DecidedAt)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    /// <summary>Askıda kalmış yürütmeler — bkz. <see cref="IApprovalQueue.GetStuckExecutionsAsync"/>.</summary>
    public async Task<IReadOnlyList<ApprovalRequest>> GetStuckExecutionsAsync(CancellationToken ct = default)
    {
        var approved = nameof(ApprovalStatus.Approved);
        var running = nameof(ApprovalExecutionStatus.Running);
        // Taze kayıtlar hâlâ çalışıyor olabilir; onları "askıda" diye göstermek yanlış alarmdır.
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(Math.Max(1, _options.StuckExecutionAfterMinutes));

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await ctx.Approvals.AsNoTracking()
            .Where(a => a.Status == approved && a.ExecutionStatus == running
                     && a.DecidedAt != null && a.DecidedAt < cutoff)
            .OrderBy(a => a.DecidedAt)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    /// <summary>Kalıcı depodan tek kayıt — gerekçe için bkz. <see cref="IApprovalQueue.GetAsync"/>.</summary>
    public async Task<ApprovalRequest?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var row = await ctx.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return row is null ? null : ToDomain(row);
    }

    /// <summary>
    /// Yalnızca <c>customer_seen_at</c> kolonunu yazar — kaydın geri kalanına DOKUNMAZ.
    ///
    /// <para>
    /// Bu ayrım kritik: burası eskiden cache'teki nesnenin TÜM alanlarını (Status, DecidedAt,
    /// ExecutionResult...) DB'ye geri yazan genel bir UPDATE çağırıyordu. Kararı kaçırmış bir
    /// pod'un cache'i "Pending" der; unseen listesini pod A'dan alıp "gördüm" isteği pod B'ye
    /// düşen bir istemci (iki ayrı HTTP isteği, load balancer) DB'deki Approved kaydı Pending'e
    /// GERİ ÇEVİRİRDİ. Bu yalnızca yanlış bir durum etiketi değil, kaydı yeniden "sahiplenilebilir"
    /// hâle getirdiği için mükerrer yürütme korumasını (ClaimDecisionAsync) da delerdi.
    /// </para>
    /// </summary>
    public async Task<bool> MarkSeenAsync(string id, CancellationToken ct = default)
    {
        EnsureHydrated();

        var seenAt = DateTime.UtcNow;
        try
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
            await ctx.Approvals
                .Where(a => a.Id == id && a.CustomerSeenAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.CustomerSeenAt, seenAt), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Approval CustomerSeenAt UPDATE başarısız. Id={Id}", id);
            return false;
        }

        if (_entries.TryGetValue(id, out var entry) && entry.Request.CustomerSeenAt is null)
            entry.Request.CustomerSeenAt = seenAt;

        return true;
    }

    /// <summary>Doğrudan PostgreSQL'e sorar — gerekçe için bkz. <see cref="GetUnseenForSessionAsync"/>.</summary>
    public async Task<IReadOnlyList<ApprovalRequest>> GetHistoryForCustomerAsync(
        string customerId, int count = 100, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await ctx.Approvals.AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.RequestedAt)
            .Take(count)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task InsertAsync(ApprovalRequest req, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        ctx.Approvals.Add(ToEntity(req));
        await ctx.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Kararı DB'de KOŞULLU olarak yazar: yalnızca satır hâlâ Pending ise. Etkilenen satır
    /// sayısını döner (1 = bu pod kararı sahiplendi, 0 = başkası önce davrandı).
    ///
    /// <para>
    /// Tek bir <c>UPDATE ... WHERE Status = 'Pending'</c> ifadesidir, yani kontrol ile yazma
    /// arasında başka bir pod araya giremez. Bellekteki durumu okuyup sonra koşulsuz yazan
    /// eski hâli, iki pod'un aynı iadeyi sırayla iki kez yürütmesine açıktı.
    /// </para>
    /// </summary>
    private async Task<int> ClaimDecisionAsync(
        string id, ApprovalStatus newStatus, ApprovalExecutionStatus newExecStatus,
        DateTime decidedAt, string decidedBy, string? reason, CancellationToken ct)
    {
        var pending = nameof(ApprovalStatus.Pending);
        var target = newStatus.ToString();
        var targetExec = newExecStatus.ToString();

        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
        return await ctx.Approvals
            .Where(a => a.Id == id && a.Status == pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.Status, target)
                .SetProperty(a => a.ExecutionStatus, targetExec)
                .SetProperty(a => a.DecidedAt, decidedAt)
                .SetProperty(a => a.DecidedBy, decidedBy)
                .SetProperty(a => a.DecisionReason, reason), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Bir kaydın bellekteki kopyasını DB'deki gerçekle değiştirir. Sahiplenme başarısız
    /// olduğunda çağrılır: o an bayat olduğumuzu kesin olarak biliriz, dolayısıyla kendimizi
    /// onarmak için doğru andır. Kaydın Tcs'i (varsa) korunur.
    /// </summary>
    private async Task RefreshFromDbAsync(string id, CancellationToken ct)
    {
        try
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
            var row = await ctx.Approvals.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            if (row is null) return;

            var tcs = _entries.TryGetValue(id, out var existing) ? existing.Tcs : null;
            var refreshed = ToDomain(row);
            _entries[id] = new QueueEntry(refreshed, tcs);
            tcs?.TrySetResult(refreshed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Approval cache tazeleme başarısız. Id={Id}", id);
        }
    }

    /// <summary>
    /// Yürütme sonucunu yazar — yalnızca <c>execution_result</c> ve <c>executed_at</c>.
    /// Kararın kendisi zaten <see cref="ClaimDecisionAsync"/> ile yazılmıştır; burada tekrar
    /// yazmak, aradan geçen sürede değişmiş olabilecek alanları cache'ten ezmek demektir.
    /// </summary>
    private async Task WriteExecutionOutcomeAsync(ApprovalRequest req)
    {
        var result = req.ExecutionResult;
        var executedAt = req.ExecutedAt;
        var execStatus = req.ExecutionStatus.ToString();

        await using var ctx = await _dbFactory.CreateDbContextAsync();
        await ctx.Approvals
            .Where(a => a.Id == req.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.ExecutionResult, result)
                .SetProperty(a => a.ExecutedAt, executedAt)
                .SetProperty(a => a.ExecutionStatus, execStatus))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Bloklayan eski yolun timeout dalı: kaydı yalnızca hâlâ Pending ise Expired'a çeker.
    /// </summary>
    private async Task MarkExpiredIfPendingAsync(string id)
    {
        var pending = nameof(ApprovalStatus.Pending);
        var expired = nameof(ApprovalStatus.Expired);
        var now = DateTime.UtcNow;

        await using var ctx = await _dbFactory.CreateDbContextAsync();
        await ctx.Approvals
            .Where(a => a.Id == id && a.Status == pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(a => a.Status, expired)
                .SetProperty(a => a.DecidedAt, now))
            .ConfigureAwait(false);
    }

    private static ApprovalRequestEntity ToEntity(ApprovalRequest req) => new()
    {
        Id = req.Id,
        SessionId = req.SessionId,
        CustomerId = req.CustomerId,
        TraceId = req.TraceId,
        ToolName = req.ToolName,
        AgentName = req.AgentName,
        ParametersJson = System.Text.Json.JsonSerializer.Serialize(req.Parameters),
        UserQuery = req.UserQuery,
        Justification = req.Justification,
        RequestedAt = req.RequestedAt,
        DecidedAt = req.DecidedAt,
        Status = req.Status.ToString(),
        DecidedBy = req.DecidedBy,
        DecisionReason = req.DecisionReason,
        TimeoutSeconds = req.TimeoutSeconds,
        ExecutionResult = req.ExecutionResult,
        ExecutedAt = req.ExecutedAt,
        ExecutionStatus = req.ExecutionStatus.ToString(),
        CustomerSeenAt = req.CustomerSeenAt
    };

    private static ApprovalRequest ToDomain(ApprovalRequestEntity e)
    {
        var parameters = string.IsNullOrWhiteSpace(e.ParametersJson)
            ? new Dictionary<string, object?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(e.ParametersJson)
              ?? new Dictionary<string, object?>();

        var status = Enum.TryParse<ApprovalStatus>(e.Status, ignoreCase: true, out var s)
            ? s : ApprovalStatus.Pending;

        return new ApprovalRequest
        {
            Id = e.Id,
            SessionId = e.SessionId,
            CustomerId = e.CustomerId,
            TraceId = e.TraceId,
            ToolName = e.ToolName,
            AgentName = e.AgentName,
            Parameters = parameters,
            UserQuery = e.UserQuery,
            Justification = e.Justification,
            RequestedAt = e.RequestedAt,
            DecidedAt = e.DecidedAt,
            Status = status,
            DecidedBy = e.DecidedBy,
            DecisionReason = e.DecisionReason,
            TimeoutSeconds = e.TimeoutSeconds,
            ExecutionResult = e.ExecutionResult,
            ExecutedAt = e.ExecutedAt,
            ExecutionStatus = Enum.TryParse<ApprovalExecutionStatus>(e.ExecutionStatus, ignoreCase: true, out var es)
                ? es : ApprovalExecutionStatus.None,
            CustomerSeenAt = e.CustomerSeenAt
        };
    }

    // Kasıtlı olarak sync-blocking: GetPending/GetRecent/Get senkron port metotları
    // (admin panel salt-okunur uçları) olduğu için burada async'e geçmek onları da
    // async'e zorlar. Bloklama süreç ömrü boyunca yalnızca BİR KEZ (ilk çağrıda)
    // gerçekleşir — CreateAsync/DecideAsync'teki her-istekte-bir DB round-trip'i ile
    // aynı sınıfta değildir, bu yüzden kapsam dışı bırakıldı.
    private void EnsureHydrated()
    {
        if (_hydrated) return;
        lock (_hydrationLock)
        {
            if (_hydrated) return;
            try
            {
                HydrateAsync().GetAwaiter().GetResult();
                _hydrated = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HITL] Approval cache hydrate başarısız.");
            }
        }
    }

    /// <summary>
    /// Cache'i doldurur: <b>TÜM açık (Pending) kayıtlar</b> + son <see cref="HydrateRecentCount"/>
    /// karara bağlanmış kayıt.
    ///
    /// <para>
    /// Pending'lerin tamamı şart. Burası eskiden tarihe göre son 200 kaydı çekiyordu (yorum
    /// "tüm açık kayıtlar" dese de kod bunu yapmıyordu): yoğun bir kurulumda 200 yeni kaydın
    /// gerisinde kalan eski bir Pending onay cache'e hiç girmez, dolayısıyla admin panelinde
    /// görünmez ve <c>StaleApprovalSweepService</c> onu bulamayacağı için 72 saat sonra da
    /// reddedilmezdi — kayıt sonsuza kadar askıda kalırdı. Karara bağlanmışlarda sınır zararsız,
    /// çünkü onlar üzerinde artık iş yapılmıyor.
    /// </para>
    /// </summary>
    private async Task HydrateAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var pending = nameof(ApprovalStatus.Pending);

        var open = await ctx.Approvals.AsNoTracking()
            .Where(a => a.Status == pending)
            .ToListAsync();

        var decided = await ctx.Approvals.AsNoTracking()
            .Where(a => a.Status != pending)
            .OrderByDescending(a => a.RequestedAt)
            .Take(HydrateRecentCount)
            .ToListAsync();

        var rows = open.Concat(decided).ToList();

        foreach (var e in rows)
        {
            // TryAdd — indeksleyici DEĞİL. Hydrate, CreateAsync ile aynı anda çalışabilir:
            // indeksleyici kullanıldığında az önce oluşturulmuş bir kaydın girdisi, TCS'i
            // null olan bir kopyayla eziliyordu. O kaydı bekleyen AwaitDecisionAsync'in
            // TaskCompletionSource'u böylece kayboluyor ve karar geldiğinde hiçbir zaman
            // tamamlanmıyordu. Cache'teki girdi her zaman en az DB satırı kadar tazedir.
            _entries.TryAdd(e.Id, new QueueEntry(ToDomain(e), tcs: null));
        }

        _logger.LogInformation("[HITL] Approval cache hydrate: {Count} kayıt", rows.Count);
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void OnRemoteCreated(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var id = root.GetProperty("id").GetString()!;
            if (_entries.ContainsKey(id)) return;

            var parametersJson = root.GetProperty("parametersJson").GetString() ?? "{}";
            var parameters = JsonSerializer.Deserialize<Dictionary<string, object?>>(parametersJson)
                             ?? new Dictionary<string, object?>();

            var req = new ApprovalRequest
            {
                Id = id,
                SessionId = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null,
                CustomerId = root.TryGetProperty("customerId", out var cid) ? cid.GetString() : null,
                TraceId = root.TryGetProperty("traceId", out var tr) ? tr.GetString() : null,
                ToolName = root.GetProperty("toolName").GetString() ?? "",
                AgentName = root.TryGetProperty("agentName", out var an) ? an.GetString() : null,
                Parameters = parameters,
                UserQuery = root.TryGetProperty("userQuery", out var uq) ? uq.GetString() : null,
                Justification = root.TryGetProperty("justification", out var j) ? j.GetString() : null,
                RequestedAt = root.GetProperty("requestedAt").GetDateTime(),
                TimeoutSeconds = root.GetProperty("timeoutSeconds").GetInt32(),
                Status = ApprovalStatus.Pending
            };

            _entries.TryAdd(req.Id, new QueueEntry(req, tcs: null));

            try { RequestCreated?.Invoke(this, req); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestCreated handler (remote) failed"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis OnRemoteCreated parse hatası");
        }
    }

    private void OnRemoteDecided(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var id = root.GetProperty("id").GetString()!;
            if (!_entries.TryGetValue(id, out var entry)) return;

            // Tcs'in VARLIĞINA BAKILMAZ. Burada eskiden "entry.Tcs is null → return" vardı;
            // bloklayan modelde makuldü çünkü mesajın tek amacı bekleyen bir çağrıyı uyandırmaktı.
            // Bloklamayan modelde hiç kimse beklemiyor, dolayısıyla kararı VERMEYEN pod'larda
            // Tcs her zaman null olur (bkz. OnRemoteCreated ve HydrateAsync: tcs: null) — o hâliyle
            // bu satır kararın diğer pod'lara YAYILMASINI tümden engelliyordu: bellekte kayıt
            // Pending kalıyor, RequestDecided hiç tetiklenmiyor (SSE bildirimi gitmiyor) ve
            // GetUnseenForSession'ın "Status != Pending" filtresi kaydı eleyerek müşteri sonradan
            // girdiğinde de göstermiyordu.
            //
            // Mesaj tekrarına karşı koruma. "Status != Pending ise atla" demek YETMEZ: bir pod
            // kaydı tam yürütme sırasında hydrate etmişse belleğinde zaten Approved+Running olur
            // ve o zaman yürütmenin BİTTİĞİNİ bildiren bu mesajı tümden atlardı — müşteriye
            // başarısız bir işlem anlık olarak düz "Onaylandı" görünürdü. Bu yüzden ölçüt
            // "karar biliniyor mu" değil, "yürütme sonucu biliniyor mu"dur.
            var alreadyFinal =
                entry.Request.Status != ApprovalStatus.Pending &&
                entry.Request.ExecutionStatus is not ApprovalExecutionStatus.Running;
            if (alreadyFinal) return;

            var approved = root.GetProperty("approved").GetBoolean();
            entry.Request.Status = approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            entry.Request.DecidedAt = root.TryGetProperty("decidedAt", out var da) && da.ValueKind != JsonValueKind.Null
                ? da.GetDateTime() : DateTime.UtcNow;
            entry.Request.DecidedBy = root.TryGetProperty("decidedBy", out var db) ? db.GetString() : null;
            entry.Request.DecisionReason = root.TryGetProperty("reason", out var r) && r.ValueKind != JsonValueKind.Null
                ? r.GetString() : null;
            entry.Request.ExecutionResult = root.TryGetProperty("executionResult", out var er) && er.ValueKind != JsonValueKind.Null
                ? er.GetString() : null;
            entry.Request.ExecutedAt = root.TryGetProperty("executedAt", out var ea) && ea.ValueKind != JsonValueKind.Null
                ? ea.GetDateTime() : null;
            entry.Request.ExecutionStatus =
                root.TryGetProperty("executionStatus", out var esr) && esr.ValueKind == JsonValueKind.String
                && Enum.TryParse<ApprovalExecutionStatus>(esr.GetString(), ignoreCase: true, out var remoteExec)
                    ? remoteExec : ApprovalExecutionStatus.None;

            entry.Tcs?.TrySetResult(entry.Request);

            try { RequestDecided?.Invoke(this, entry.Request); }
            catch (Exception ex) { _logger.LogWarning(ex, "RequestDecided handler (remote) failed"); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HITL] Redis OnRemoteDecided parse hatası");
        }
    }

    private void PublishRedis(string channel, object payload)
    {
        _messageBus.Publish(channel, JsonSerializer.Serialize(payload));
    }

    // ─────────────────────────────────────────────────────────────────────────

    private sealed class QueueEntry
    {
        public ApprovalRequest Request { get; }
        public TaskCompletionSource<ApprovalRequest>? Tcs { get; }

        public QueueEntry(ApprovalRequest req, TaskCompletionSource<ApprovalRequest>? tcs)
        {
            Request = req;
            Tcs = tcs;
        }
    }
}

