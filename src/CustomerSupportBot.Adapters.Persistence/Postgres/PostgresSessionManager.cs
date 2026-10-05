// Services/Persistence/PostgresSessionManager.cs
// Hibrit cache + PostgreSQL oturum yöneticisi.
// ISessionManager implementasyonu — tamamen async (sync-over-async blocking yok).
//
// Davranış (in-memory ile aynı API ve semantik):
//   - Cache: ConcurrentDictionary<sessionId, AgentSession> + message history.
//   - AddExchange: cache + DB (UPSERT session, INSERT 2 message).
//   - AppendAssistantMessage: son boş assistant mesajını UPDATE veya INSERT.
//   - ExtractAndUpdateState: Mevcut in-memory mantık aynen — sonra UPSERT session.
//   - ClearSession: cache + DB DELETE (cascade → messages).
//   - GetAllSessions: cache (her session ilk kullanımda lazy hydrate).
//   - Cache SINIRLIDIR: maxCachedSessions aşılınca en uzun süredir erişilmeyen oturumlar
//     çıkarılır (minIdleBeforeEviction içinde erişilenler hariç) ve sonraki erişimde DB'den
//     geri yüklenir — bkz. TrimIfOverCapacity.
//
// Basitlik için ExtractAndUpdateState içindeki regex/duygu/intent kuralları
// InMemorySessionManager'dan birebir kopyalandı (tek doğruluk kaynağı için
// ortak helper'a refactor Faz 4 cleanup'ında düşünülebilir).
//
// Yatay ölçeklendirme (Redis pub/sub):
//   - Session state (küçük, sınırlı boyut) Update() sonrası TAM olarak yayınlanır —
//     ChatModeRegistry/EscalationSink'teki desenle aynı.
//   - Mesaj geçmişi TAM listeyi değil, sadece DELTA'yı yayınlar (her mesajda büyüyen
//     bir listenin tamamını göndermek israf olurdu). Uzak pod bu session'ı daha önce
//     hiç görmediyse delta'yı yok sayar — ilk gerçek erişimde EnsureSessionHydrated
//     zaten DB'den TAM geçmişi çekip cache'i baştan kuracaktır (bkz. HydrateSessionAsync,
//     list.Clear() + DB'den yeniden doldurma), yani delta'dan doğan eksik/kısmi liste
//     kendi kendini onarır.

using System.Collections.Concurrent;
using System.Text.Json;
using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

public sealed class PostgresSessionManager : ISessionManager
{
    private const string ChannelSessionUpdated = "csbot:session:updated";
    private const string ChannelHistoryChanged = "csbot:session:history";
    private const string ChannelSessionCleared = "csbot:session:cleared";

    private readonly IDbContextFactory<CustomerSupportDbContext> _dbFactory;
    private readonly ILogger<PostgresSessionManager> _logger;
    private readonly IAppDistributedLock _distributedLock;
    private readonly IMessageBusPort _messageBus;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, List<ConversationMessage>> _messageHistory = new();
    /// <summary>
    /// Session başına hydrate işi. Değer bir <b>bayrak değil, işin kendisidir</b>: eşzamanlı
    /// çağıranlar aynı Task'ı bekler. Bayrak kullanıldığında ikinci çağıran "biri hydrate
    /// ediyor" bilgisiyle hemen dönüyor ve hydrate BİTMEDEN boş bir session'la devam ediyordu
    /// — bkz. <see cref="EnsureSessionHydratedAsync"/>.
    ///
    /// <para>
    /// Task'ın sonucu oturumun DB'de BULUNUP bulunmadığıdır. Bulunamadıysa girdi silinir —
    /// "yok" kalıcı bir karar değildir, oturum sonradan başka bir pod'da oluşabilir.
    /// </para>
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _hydratedSessions = new();

    /// <summary>
    /// Bu pod'da oluşturulan oturumun işareti: cache'teki nesne zaten gerçek kaynaktır, DB'den
    /// yeniden hydrate edilmemeli — edilseydi <c>_sessions</c>'taki nesne yenisiyle değişir ve
    /// çağıranların elindeki referans (kilit anahtarı ve durum taşıyıcısı) sessizce eskirdi.
    /// </summary>
    private static readonly Lazy<Task<bool>> CreatedLocally = new(Task.FromResult(true));
    private readonly SemaphoreSlim _allHydrationGate = new(1, 1);
    private volatile bool _allListHydrated;
    private readonly int _maxCachedSessions;
    private readonly TimeSpan _minIdleBeforeEviction;

    /// <summary>
    /// Oturum başına son erişim anı (UTC tick). Eviction en uzun süredir erişilmeyeni önce
    /// çıkarır. <c>LastActivity</c> kullanılamaz: o yalnızca YAZMADA ilerler, oysa bir tur
    /// oturumu dakikalarca okur — yazmadan önce çıkarılırsa elindeki referans eskir.
    /// </summary>
    private readonly ConcurrentDictionary<string, long> _lastAccess = new();
    private int _trimming;

    public PostgresSessionManager(
        IDbContextFactory<CustomerSupportDbContext> dbFactory,
        IAppDistributedLock distributedLock,
        IMessageBusPort messageBus,
        ILogger<PostgresSessionManager> logger,
        int maxCachedSessions = 2000,
        TimeSpan? minIdleBeforeEviction = null)
    {
        _dbFactory = dbFactory;
        _distributedLock = distributedLock;
        _messageBus = messageBus;
        _logger = logger;
        _maxCachedSessions = Math.Max(1, maxCachedSessions);
        _minIdleBeforeEviction = minIdleBeforeEviction ?? TimeSpan.FromMinutes(15);
        _messageBus.Subscribe(ChannelSessionUpdated, OnRemoteSessionUpdated);
        _messageBus.Subscribe(ChannelHistoryChanged, OnRemoteHistoryChanged);
        _messageBus.Subscribe(ChannelSessionCleared, OnRemoteSessionCleared);
    }

    // ─── ISessionManager ───

    public async Task<AgentSession> GetOrCreateAsync(string? sessionId, CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString();
        var hydration = await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            Touch(sessionId);
            return existing;
        }

        // Hydrate HATAYLA bittiyse oturumun DB'de olup olmadığını bilmiyoruz. Burada yeni boş
        // bir oturum oluşturup UPSERT etmek, DB'deki gerçek state'i (müşteri sahipliği, özet,
        // duygu geçmişi) {} ile ezerdi — örneğin pod yeniden başladıktan sonraki ilk istekte
        // anlık bir DB hatası ya da şema değişikliği yüzünden okunamayan bir StateJson yeterdi.
        // "Yok" ile "okunamadı" ayrımı burada yapılır: ikincisinde tur başarısız olur, veri
        // kaybolmaz.
        if (!hydration.Succeeded)
            throw ExceptionTranslator.Translate(hydration.Error!, $"Session yüklenemedi: {sessionId}");

        var session = new AgentSession
        {
            SessionId = sessionId,
            CreatedAt = DateTime.Now,
            LastActivity = DateTime.Now,
            State = new SessionState()
        };

        if (_sessions.TryAdd(sessionId, session))
        {
            _hydratedSessions[sessionId] = CreatedLocally;
            try { await UpsertSessionAsync(session).ConfigureAwait(false); }
            catch (Exception ex)
            {
                // Kaydedilemeyen oturum cache'te KALMAMALI: kalsaydı sonraki çağrılar DB'de hiç
                // var olmayan bir nesneyle "başarılı" devam ederdi. Yalnızca bu çağrının eklediği
                // nesne kaldırılır (anahtar+değer eşleşmesi).
                _sessions.TryRemove(new KeyValuePair<string, AgentSession>(sessionId, session));
                _hydratedSessions.TryRemove(new KeyValuePair<string, Lazy<Task<bool>>>(sessionId, CreatedLocally));
                _logger.LogError(ex, "[Session] UPSERT (create) başarısız. Id={Id}", sessionId);
                throw ExceptionTranslator.Translate(ex, $"Session oluşturulamadı: {sessionId}");
            }
            Touch(sessionId);
            return session;
        }

        // Eşzamanlı başka bir çağrı araya girdi — onun eklediği nesneyi kullan.
        return _sessions.TryGetValue(sessionId, out var winner) ? winner : session;
    }

    public async Task<AgentSession?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);
        return _sessions.TryGetValue(sessionId, out var s) ? s : null;
    }

    public async Task UpdateAsync(AgentSession session, CancellationToken ct = default)
    {
        long revision;
        lock (session)
        {
            revision = session.State.Revision + 1;
            session.State.Revision = revision;
            session.LastActivity = DateTime.Now;
        }
        _sessions[session.SessionId] = session;
        try { await UpsertSessionAsync(session).ConfigureAwait(false); }
        catch (Exception ex)
        {
            // Yazılamayan revizyon geri alınır: kalsaydı yerel sayaç DB'nin önüne geçer ve
            // başka bir pod'un bir sonraki GERÇEK güncellemesi "eski" sanılıp reddedilirdi.
            lock (session)
            {
                if (session.State.Revision == revision)
                    session.State.Revision = revision - 1;
            }
            _logger.LogError(ex,
                "[Session] UPSERT başarısız. Id={Id}", session.SessionId);
            throw ExceptionTranslator.Translate(ex, $"Session güncellenemedi: {session.SessionId}");
        }
        PublishSessionUpdated(session);
    }

    public async Task<IReadOnlyList<AgentSession>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureAllListHydratedAsync(ct).ConfigureAwait(false);
        return _sessions.Values.ToList();
    }

    public async Task ExtractAndUpdateStateAsync(
        string sessionId, string userMessage, string botResponse, CancellationToken ct = default)
    {
        var session = await GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        // Lock gerekmez — aynı session için aynı anda tek bot pipeline çalışır
        // (ConcurrentDictionary + in-memory cache). Sync-over-async lock pattern
        // thread pool starvation'a neden oluyordu.
        await ExtractAndUpdateStateCoreAsync(session, userMessage, botResponse, null, ct).ConfigureAwait(false);
    }

    public async Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct = default)
    {
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));
        var session = await GetAsync(sessionId, ct).ConfigureAwait(false);
        if (session is null) return;

        await using var handle = await _distributedLock
            .AcquireAsync($"session:{sessionId}", ct: ct)
            .ConfigureAwait(false);

        mutator(session.State);
        await UpdateAsync(session, ct).ConfigureAwait(false);
    }

    private async Task ExtractAndUpdateStateCoreAsync(
        AgentSession session, string userMessage, string botResponse,
        IReadOnlyList<ConversationMessage>? priorHistory, CancellationToken ct,
        TurnSignals? signals = null)
    {
        // ConsecutiveNegativeTurns oku-değiştir-yaz içerdiği için kilitli: aynı session'a
        // çakışan iki eşzamanlı istek (çift-submit, çoklu sekme) birbirinin artışını ezerse
        // otomatik eskalasyon eşiği bir tur geç tetiklenir. GetOrCreateAsync/GetAsync aynı
        // sessionId için hep AYNI AgentSession referansını döndürdüğünden session nesnesi
        // kilit anahtarı olarak güvenlidir (bkz. WorkflowMessageBuilder.ConsumeForceReplanHint).
        lock (session)
        {
            SessionStateExtractor.ExtractAndApply(session.State, userMessage, botResponse, priorHistory, signals);
        }
        await UpdateAsync(session, ct).ConfigureAwait(false);
    }

    // ─── Konuşma geçmişi ───

    public async Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);
        await ReconcileHistoryIfStaleAsync(sessionId, ct).ConfigureAwait(false);

        if (_messageHistory.TryGetValue(sessionId, out var history))
        {
            lock (history)
            {
                return new List<ConversationMessage>(history);
            }
        }
        return new List<ConversationMessage>();
    }

    /// <summary>
    /// Bu pod'un mesaj geçmişi cache'i DB'nin GERİSİNDE mi — ucuz bir sayım ile denetler ve
    /// öyleyse yalnızca o session'ın mesaj listesini DB'den yeniden yükler.
    ///
    /// <para>
    /// Geçmiş, pod'lar arasında DELTA olarak Redis pub/sub ile yayılır (bkz.
    /// <see cref="PublishHistoryAppended"/>). Pub/sub en fazla bir kez teslim eder; bir mesaj
    /// kaybolursa bu pod'un cache'i o andan itibaren KALICI olarak eksik kalır — hydrate yalnızca
    /// bu session ilk görüldüğünde bir kez çalışır, sonrasında bir daha DB'ye bakılmaz. Eksik
    /// geçmiş, GetHistoryAsync her turda ajana verilen bağlamın kendisi olduğu için sessizce
    /// bozuk bir konuşma bağlamına dönüşür — onay kuyruğundaki bir kayıp gibi "bir liste eksik
    /// eleman içerir" değil, ajanın konuşmayı YANLIŞ ANLAMASI demektir.
    /// </para>
    ///
    /// <para>
    /// Her turda tüm mesaj metnini DB'den çekmek (approval'daki <c>GetPendingAsync</c> gibi)
    /// bu yolun sıklığında (her GetHistoryAsync çağrısı) gereksiz maliyetlidir. Bunun yerine
    /// yalnızca SAYIM karşılaştırılır — ucuz, indeksli bir sorgu — ve yalnızca sayım
    /// uyuşmadığında (gerçekten kayıp varsa) tam metin çekilir.
    /// </para>
    /// </summary>
    private async Task ReconcileHistoryIfStaleAsync(string sessionId, CancellationToken ct)
    {
        if (!_messageHistory.TryGetValue(sessionId, out var history)) return;

        int localCount;
        lock (history) { localCount = history.Count; }

        long dbCount;
        try
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            dbCount = await ctx.Messages.LongCountAsync(m => m.SessionId == sessionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Sayım sorgusu başarısızsa eldeki (bayat olabilecek) cache ile devam ederiz —
            // bu bir OPTİMİZASYONdur, turun kendisini engellememelidir.
            _logger.LogWarning(ex, "[Session] Geçmiş uzlaştırma sayımı başarısız. Session={Session}", sessionId);
            return;
        }

        if (dbCount <= localCount) return;

        _logger.LogWarning(
            "[Session] Geçmiş cache'i DB'nin gerisinde — yeniden yükleniyor. Session={Session} local={Local} db={Db}",
            sessionId, localCount, dbCount);
        await HydrateSessionAsync(sessionId).ConfigureAwait(false);
    }

    public async Task AddExchangeAsync(
        string sessionId, string userQuery, string assistantResponse,
        TurnSignals? signals = null, CancellationToken ct = default)
    {
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        List<ConversationMessage> priorHistorySnapshot;
        lock (history)
        {
            // Bu turdan ÖNCEKİ geçmiş — SessionStateExtractor'ın bağlam takibi için
            // (eklemeden önce alınmalı, yoksa "önceki tur" bu turun kendisi olur).
            priorHistorySnapshot = new List<ConversationMessage>(history);
            history.Add(new ConversationMessage(ConversationRoles.User, userQuery));
            history.Add(new ConversationMessage(ConversationRoles.Assistant, assistantResponse));
        }

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try { await InsertExchangeAsync(sessionId, userQuery, assistantResponse).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AddExchange INSERT başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Mesaj kaydedilemedi: {sessionId}");
        }
        PublishHistoryAppended(sessionId,
        [
            new ConversationMessage(ConversationRoles.User, userQuery),
            new ConversationMessage(ConversationRoles.Assistant, assistantResponse)
        ]);

        await ExtractAndUpdateStateCoreAsync(session, userQuery, assistantResponse, priorHistorySnapshot, ct, signals)
            .ConfigureAwait(false);
    }

    public async Task AppendAssistantMessageAsync(string sessionId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        bool replacedLastEmpty;
        lock (history)
        {
            replacedLastEmpty =
                history.Count > 0
                && history[^1].Role == ConversationRoles.Assistant
                && string.IsNullOrEmpty(history[^1].Text);

            if (replacedLastEmpty)
            {
                history[^1] = new ConversationMessage(ConversationRoles.Assistant, text);
            }
            else
            {
                history.Add(new ConversationMessage(ConversationRoles.Assistant, text));
            }
        }

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try
        {
            await AppendAssistantMessageDbAsync(sessionId, text, replacedLastEmpty).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AppendAssistantMessage DB başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Assistant mesajı kaydedilemedi: {sessionId}");
        }

        var assistantMsg = new ConversationMessage(ConversationRoles.Assistant, text);
        if (replacedLastEmpty)
            PublishHistoryReplacedLast(sessionId, assistantMsg);
        else
            PublishHistoryAppended(sessionId, [assistantMsg]);
    }

    public async Task AppendUserMessageAsync(string sessionId, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        await EnsureSessionHydratedAsync(sessionId, ct).ConfigureAwait(false);

        var history = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        lock (history)
        {
            history.Add(new ConversationMessage(ConversationRoles.User, text));
        }

        var session = await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);
        session.LastActivity = DateTime.Now;
        _sessions[sessionId] = session;

        try
        {
            await AppendUserMessageDbAsync(sessionId, text).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] AppendUserMessage DB başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Kullanıcı mesajı kaydedilemedi: {sessionId}");
        }
        PublishHistoryAppended(sessionId, [new ConversationMessage(ConversationRoles.User, text)]);
    }

    public async Task<IReadOnlyList<string>> GetInactiveSessionIdsAsync(
        DateTime lastActivityBeforeUtc, int limit, CancellationToken ct = default)
    {
        // Kalıcı depodan: önbellekte olmayan (çoktan çıkarılmış) eski oturumlar da bulunmalı.
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.Sessions.AsNoTracking()
            .Where(s => s.LastActivity < lastActivityBeforeUtc)
            .OrderBy(s => s.LastActivity)
            .Take(Math.Max(0, limit))
            .Select(s => s.SessionId)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearSessionAsync(string sessionId, CancellationToken ct = default)
    {
        Evict(sessionId);

        try { await DeleteSessionAsync(sessionId).ConfigureAwait(false); }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Session] ClearSession DB başarısız. Session={Session}", sessionId);
            throw ExceptionTranslator.Translate(ex, $"Session silinemedi: {sessionId}");
        }
        PublishSessionCleared(sessionId);
    }

    public async Task<List<SessionInfo>> GetAllSessionsAsync(
        string? forCustomerId = null, CancellationToken ct = default)
    {
        await EnsureAllListHydratedAsync(ct).ConfigureAwait(false);

        var result = new List<SessionInfo>();
        foreach (var kvp in _sessions)
        {
            var session = kvp.Value;

            // Müşteri kapsamı: yalnızca bu müşteriye BAĞLI oturumlar. Bağlanmamış (anonim)
            // oturumlar da elenir — başkasının başlattığı, henüz kimliğe bağlanmamış bir
            // oturumun kimliği sızmamalı.
            if (forCustomerId is not null &&
                !string.Equals(session.State.AuthenticatedCustomerId, forCustomerId, StringComparison.Ordinal))
                continue;

            var history = await GetHistoryAsync(kvp.Key, ct).ConfigureAwait(false);
            var firstUserMsg = history.FirstOrDefault(m => m.Role == ConversationRoles.User)?.Text;

            result.Add(new SessionInfo
            {
                SessionId = session.SessionId,
                Title = firstUserMsg is not null
                    ? (firstUserMsg.Length > 50 ? firstUserMsg[..50] + "..." : firstUserMsg)
                    : WellKnown.FallbackMessages.NewChat,
                LastActivity = session.LastActivity,
                MessageCount = history.Count
            });
        }

        return result.OrderByDescending(s => s.LastActivity).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DB IO
    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertSessionAsync(AgentSession session)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        var existing = await ctx.Sessions.FirstOrDefaultAsync(s => s.SessionId == session.SessionId);
        var stateJson = JsonSerializer.Serialize(session.State);

        if (existing is null)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = session.SessionId,
                CreatedAt = session.CreatedAt.Kind == DateTimeKind.Utc ? session.CreatedAt : session.CreatedAt.ToUniversalTime(),
                LastActivity = session.LastActivity.Kind == DateTimeKind.Utc ? session.LastActivity : session.LastActivity.ToUniversalTime(),
                StateJson = stateJson
            });
        }
        else
        {
            // Yazma hâlâ "son yazan kazanır": tam StateJson üzerine yazılır. Cache tarafında eski
            // snapshot'lar reddediliyor (bkz. ApplySnapshot), ama DB'de daha YENİ bir revizyonun
            // üzerine yazılması — iki yazıcının aynı temelden başlaması — burada engellenmez,
            // yalnızca görünür kılınır. Gerçek çözüm mutasyon-tabanlı yazma + koşullu UPDATE'tir.
            var storedRevision = ReadRevision(existing.StateJson);
            if (storedRevision >= session.State.Revision && session.State.Revision > 0)
            {
                _logger.LogWarning(
                    "[Session] Eşzamanlı yazma: DB'deki revizyon ({Stored}) yazılan revizyondan ({Incoming}) "
                  + "küçük değil — daha yeni bir state'in üzerine yazılıyor olabilir. Id={Id}",
                    storedRevision, session.State.Revision, session.SessionId);
            }

            existing.LastActivity = session.LastActivity.Kind == DateTimeKind.Utc ? session.LastActivity : session.LastActivity.ToUniversalTime();
            existing.StateJson = stateJson;
        }

        await ctx.SaveChangesAsync();
    }

    private static long ReadRevision(string? stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(stateJson);
            return doc.RootElement.TryGetProperty(nameof(SessionState.Revision), out var r)
                && r.TryGetInt64(out var value) ? value : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private async Task InsertExchangeAsync(string sessionId, string userQuery, string assistantResponse)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        // Session'ı garanti altına al (FK için)
        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        var now = DateTime.UtcNow;
        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "user",
            Text = userQuery,
            CreatedAt = now
        });
        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = assistantResponse,
            CreatedAt = now.AddMilliseconds(1)
        });

        await ctx.SaveChangesAsync();
    }

    private async Task AppendAssistantMessageDbAsync(string sessionId, string text, bool replaceLastEmpty)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        if (replaceLastEmpty)
        {
            // Aynı session'daki son assistant mesajı boş ise UPDATE
            var lastAssistant = await ctx.Messages
                .Where(m => m.SessionId == sessionId && m.Role == "assistant")
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            if (lastAssistant is not null && string.IsNullOrEmpty(lastAssistant.Text))
            {
                lastAssistant.Text = text;
                lastAssistant.CreatedAt = DateTime.UtcNow;
                await ctx.SaveChangesAsync();
                return;
            }
            // Bulunmazsa INSERT'e düş.
        }

        // Session ensure
        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "assistant",
            Text = text,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task AppendUserMessageDbAsync(string sessionId, string text)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var sessionExists = await ctx.Sessions.AnyAsync(s => s.SessionId == sessionId);
        if (!sessionExists)
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivity = DateTime.UtcNow,
                StateJson = "{}"
            });
        }

        ctx.Messages.Add(new MessageEntity
        {
            SessionId = sessionId,
            Role = "user",
            Text = text,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task DeleteSessionAsync(string sessionId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        // Cascade: session silinirse mesajlar otomatik silinir (FK).
        var session = await ctx.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null) return;
        ctx.Sessions.Remove(session);
        await ctx.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Hydration
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Oturumu DB'den yeniden okur — gerekçe için bkz. <see cref="ISessionManager.ReloadAsync"/>.</summary>
    public async Task<AgentSession> ReloadAsync(string sessionId, CancellationToken ct = default)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync(ct);

        var row = await ctx.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, ct);

        // Kayıt yoksa (henüz yazılmamış yeni oturum) elimizdeki cache nesnesi geçerlidir.
        if (row is null)
            return await GetOrCreateAsync(sessionId, ct).ConfigureAwait(false);

        var state = string.IsNullOrWhiteSpace(row.StateJson) || row.StateJson == "{}"
            ? new SessionState()
            : JsonSerializer.Deserialize<SessionState>(row.StateJson) ?? new SessionState();

        var current = ApplySnapshot(sessionId, row.CreatedAt, row.LastActivity, state);
        Touch(sessionId);
        return current;
    }

    /// <summary>
    /// Dışarıdan gelen bir state snapshot'ını (DB okuması veya başka pod'un yayını) cache'e
    /// uygular — <b>nesneyi değiştirmeden, yerinde</b>.
    ///
    /// <para>
    /// Eskiden her reload/hydrate/pub-sub mesajı <c>_sessions[id]</c>'yi YENİ bir
    /// <see cref="AgentSession"/> ile değiştiriyordu. Oysa çağıranlar elde tuttukları nesneyi hem
    /// kilit anahtarı (<c>lock (session)</c>) hem durum taşıyıcısı olarak kullanır: değişimden
    /// sonra iki çağıran farklı nesnelere kilitlenip birbirini dışlamıyor, eski nesneyi tutan
    /// tur da bayat state okuyor ve onu yazarak yeni güncellemeyi eziyordu. Artık tek bir nesne
    /// kalır, içeriği kilit altında güncellenir.
    /// </para>
    ///
    /// <para>
    /// Revizyonu cache'tekinden DÜŞÜK snapshot uygulanmaz: pub/sub sırasız teslim edebilir ve
    /// geç gelen eski bir mesaj daha yeni durumu ezmemelidir. Eşit revizyon uygulanır (aynı
    /// yazmanın DB'den okunmuş hâli).
    /// </para>
    /// </summary>
    private AgentSession ApplySnapshot(
        string sessionId, DateTime createdAt, DateTime lastActivity, SessionState state)
    {
        while (true)
        {
            if (_sessions.TryGetValue(sessionId, out var existing))
            {
                lock (existing)
                {
                    if (state.Revision < existing.State.Revision)
                    {
                        _logger.LogDebug(
                            "[Session] Eski snapshot uygulanmadı. Id={Id} gelen={Incoming} mevcut={Current}",
                            sessionId, state.Revision, existing.State.Revision);
                        return existing;
                    }

                    existing.CreatedAt = createdAt;
                    existing.LastActivity = lastActivity;
                    existing.State = state;
                }
                return existing;
            }

            var fresh = new AgentSession
            {
                SessionId = sessionId,
                CreatedAt = createdAt,
                LastActivity = lastActivity,
                State = state
            };
            if (_sessions.TryAdd(sessionId, fresh))
                return fresh;
        }
    }

    /// <summary>
    /// Session'ın DB'den yüklenmesini garanti eder — <b>tamamlanana kadar bekleyerek</b>.
    ///
    /// <para>
    /// Eskiden bir bayrak vardı ve bayrak hydrate BAŞLAMADAN önce konuyordu: ikinci eşzamanlı
    /// çağrı <c>TryAdd</c>'den false alıp hemen dönüyor, yani DB okuması sürerken boş bir
    /// session'la devam ediyordu. Bunun bedeli kalıcıdır — o boş <c>State</c> üzerinden yapılan
    /// bir yazma, DB'deki gerçek state'i (müşteri sahipliği dahil) <c>{}</c> ile ezer. Aynı
    /// oturuma iki isteğin hemen ardışık gelmesi bunun için yeterlidir.
    /// </para>
    ///
    /// <para>
    /// Değer artık işin kendisi olduğu için ikinci çağıran aynı Task'ı bekler; hydrate bir kez
    /// çalışır ve herkes tamamlanmış state'i görür.
    /// </para>
    ///
    /// <para>
    /// Oturum DB'de YOKSA girdi de silinir. Eskiden "bulunamadı" sonucu da kalıcı olarak
    /// cache'leniyordu: ilk mesajdan önce açılan bir olay akışı (<c>/chat/events/{id}</c>)
    /// oturumu bu pod'da "yok" diye mühürlüyor, oturum sonradan başka bir pod'da oluştuğunda
    /// bu pod onu bir daha okumuyordu — <c>GetOrCreateAsync</c> boş state'li yeni bir nesne
    /// üretip DB'deki gerçek state'i (müşteri sahipliği dahil) ezerdi. Rastgele id'lerle
    /// yapılan sorgular da süreç ömrü boyunca birikirdi.
    /// </para>
    ///
    /// <para>
    /// Silme işlemleri yalnızca BU çağrının eklediği girdiyi kaldırır (anahtar+değer eşleşmesi):
    /// arada <see cref="GetOrCreateAsync"/> oturumu oluşturup kendi işaretini koyduysa o korunur.
    /// </para>
    /// </summary>
    private async Task<HydrationResult> EnsureSessionHydratedAsync(string sessionId, CancellationToken ct)
    {
        var work = _hydratedSessions.GetOrAdd(
            sessionId,
            id => new Lazy<Task<bool>>(
                () => HydrateSessionAsync(id),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var entry = new KeyValuePair<string, Lazy<Task<bool>>>(sessionId, work);

        try
        {
            if (!await work.Value.ConfigureAwait(false))
            {
                _hydratedSessions.TryRemove(entry);
                return HydrationResult.NotFound;
            }

            if (_sessions.ContainsKey(sessionId))
                Touch(sessionId);
            return HydrationResult.Found;
        }
        catch (Exception ex)
        {
            // Girdiyi geri al — aksi halde geçici bir DB hatası (timeout, deadlock) bu
            // session'ı process ömrü boyunca "hydrate edildi ama boş" olarak kalıcı hale
            // getirir; bir sonraki istek DB'yi tekrar denemeden geçmişsiz devam eder.
            // Not: başarısız Task cache'lendiği için girdinin KALDIRILMASI şart — Lazy
            // aynı hatayı sonsuza kadar yeniden fırlatırdı.
            //
            // Hata burada YUTULUR ama sonuç olarak döner: okuma yolları (GetAsync, geçmiş)
            // eldeki cache'le — ör. pub/sub'dan gelen state ile — devam edebilir; oluşturma
            // yolu (GetOrCreateAsync) ise "oturum yok" ile "okunamadı"yı ayırt etmek zorunda.
            _hydratedSessions.TryRemove(entry);
            _logger.LogError(ex, "[Session] Hydrate başarısız. Id={Id}", sessionId);
            return HydrationResult.Failed(ex);
        }
    }

    private readonly record struct HydrationResult(bool Succeeded, bool Exists, Exception? Error)
    {
        public static HydrationResult Found => new(true, true, null);
        public static HydrationResult NotFound => new(true, false, null);
        public static HydrationResult Failed(Exception error) => new(false, false, error);
    }

    /// <returns>Oturum DB'de bulunduysa <c>true</c>.</returns>
    private async Task<bool> HydrateSessionAsync(string sessionId)
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();

        var sessionRow = await ctx.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (sessionRow is null) return false;

        var state = string.IsNullOrWhiteSpace(sessionRow.StateJson) || sessionRow.StateJson == "{}"
            ? new SessionState()
            : JsonSerializer.Deserialize<SessionState>(sessionRow.StateJson) ?? new SessionState();

        // Pub/sub bu oturumu hydrate'ten önce cache'e koymuş olabilir; o nesne korunur.
        ApplySnapshot(sessionRow.SessionId, sessionRow.CreatedAt, sessionRow.LastActivity, state);

        var messages = await ctx.Messages.AsNoTracking()
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Id)
            .ToListAsync();

        var list = _messageHistory.GetOrAdd(sessionId, _ => new List<ConversationMessage>());
        lock (list)
        {
            list.Clear();
            foreach (var m in messages)
            {
                var role = string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                    ? ConversationRoles.User
                    : ConversationRoles.Assistant;
                list.Add(new ConversationMessage(role, m.Text));
            }
        }

        return true;
    }

    // ─── Cache sınırı ───
    //
    // Eskiden cache'e giren hiçbir oturum çıkmıyordu: görülen her oturum ve tüm mesaj geçmişi
    // süreç ömrü boyunca bellekte kalıyordu. DB gerçek kaynak olduğu için çıkarılan oturum
    // sonraki erişimde EnsureSessionHydratedAsync ile eksiksiz geri yüklenir.

    private void Touch(string sessionId)
    {
        _lastAccess[sessionId] = DateTime.UtcNow.Ticks;
        TrimIfOverCapacity();
    }

    /// <summary>
    /// Kapasite aşıldığında en uzun süredir erişilmeyen oturumları çıkarır. Histerezis vardır:
    /// sınır aşılınca kapasitenin %90'ına inilir, böylece sıralama her yeni oturumda değil
    /// arada bir yapılır. Aynı anda tek bir tarama çalışır; diğer çağıranlar beklemeden geçer.
    ///
    /// <para>
    /// <see cref="_minIdleBeforeEviction"/>'dan yakın zamanda erişilmiş oturum ÇIKARILMAZ,
    /// sınır aşılsa bile. Bir tur oturumu dakikalarca kullanır ve aynı nesneyi kilit/durum
    /// taşıyıcısı olarak tutar; çıkarılsaydı sonraki erişim DB'den yeni bir nesne üretir ve
    /// turun elindeki referans sessizce eskirdi. Sınır bu yüzden kesin değil, yumuşaktır.
    /// </para>
    /// </summary>
    private void TrimIfOverCapacity()
    {
        if (_sessions.Count <= _maxCachedSessions) return;
        if (Interlocked.Exchange(ref _trimming, 1) == 1) return;
        try
        {
            var excess = _sessions.Count - _maxCachedSessions * 9 / 10;
            if (excess <= 0) return;

            // Erişim kaydı olmayanlar (toplu liste hydrate'i veya uzak pod güncellemesiyle
            // gelenler) en eski sayılır.
            var idleCutoff = (DateTime.UtcNow - _minIdleBeforeEviction).Ticks;
            var victims = _sessions.Keys
                .Select(id => (Id: id, At: _lastAccess.TryGetValue(id, out var t) ? t : 0L))
                .Where(x => x.At <= idleCutoff)
                .OrderBy(x => x.At)
                .Take(excess)
                .Select(x => x.Id)
                .ToList();

            foreach (var id in victims) Evict(id);

            _logger.LogDebug("[Session] Cache sınırı: {Count} oturum çıkarıldı (kapasite={Max}).",
                victims.Count, _maxCachedSessions);
        }
        finally
        {
            Volatile.Write(ref _trimming, 0);
        }
    }

    private void Evict(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _messageHistory.TryRemove(sessionId, out _);
        _hydratedSessions.TryRemove(sessionId, out _);
        _lastAccess.TryRemove(sessionId, out _);
    }

    private async Task EnsureAllListHydratedAsync(CancellationToken ct)
    {
        if (_allListHydrated) return;
        await _allHydrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_allListHydrated) return;
            await HydrateAllSessionMetadataAsync().ConfigureAwait(false);
            _allListHydrated = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Session] All-list hydrate başarısız.");
        }
        finally
        {
            _allHydrationGate.Release();
        }
    }

    private async Task HydrateAllSessionMetadataAsync()
    {
        await using var ctx = await _dbFactory.CreateDbContextAsync();
        // Sadece session metadata + ilk user message snippet alacağız.
        // Mesaj geçmişi tek tek lazy hydrate edilir (büyük session'larda gereksiz IO yapmamak için).
        var sessions = await ctx.Sessions.AsNoTracking()
            .OrderByDescending(s => s.LastActivity)
            .Take(500)
            .ToListAsync();

        foreach (var s in sessions)
        {
            if (_sessions.ContainsKey(s.SessionId)) continue;

            var state = string.IsNullOrWhiteSpace(s.StateJson) || s.StateJson == "{}"
                ? new SessionState()
                : JsonSerializer.Deserialize<SessionState>(s.StateJson) ?? new SessionState();

            // TryAdd: kontrol ile ekleme arasında oturum başka bir yoldan cache'e girmiş olabilir;
            // o nesne (elinde tutan çağıranlar varken) değiştirilmemeli.
            _sessions.TryAdd(s.SessionId, new AgentSession
            {
                SessionId = s.SessionId,
                CreatedAt = s.CreatedAt,
                LastActivity = s.LastActivity,
                State = state
            });
        }

        _logger.LogInformation("[Session] Metadata hydrate: {Count} session", sessions.Count);
    }

    // ─── Redis cross-pod handlers ─────────────────────────────────────────────

    private void PublishSessionUpdated(AgentSession session)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            sessionId = session.SessionId,
            createdAt = session.CreatedAt,
            lastActivity = session.LastActivity,
            state = session.State
        };
        _messageBus.Publish(ChannelSessionUpdated, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteSessionUpdated(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("sessionId").GetString()!;
            var state = JsonSerializer.Deserialize<SessionState>(root.GetProperty("state").GetRawText())
                ?? new SessionState();

            ApplySnapshot(
                sessionId,
                root.GetProperty("createdAt").GetDateTime(),
                root.GetProperty("lastActivity").GetDateTime(),
                state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Session] Redis OnRemoteSessionUpdated parse hatası");
        }
    }

    /// <summary>
    /// Mesaj geçmişine DELTA yayınlar (tam listeyi değil) — bkz. dosya başındaki
    /// "Yatay ölçeklendirme" notu.
    /// </summary>
    private void PublishHistoryAppended(string sessionId, IReadOnlyList<ConversationMessage> messages)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            sessionId,
            op = "append",
            messages
        };
        _messageBus.Publish(ChannelHistoryChanged, JsonSerializer.Serialize(payload));
    }

    private void PublishHistoryReplacedLast(string sessionId, ConversationMessage message)
    {
        var payload = new
        {
            nodeId = _messageBus.NodeId,
            sessionId,
            op = "replaceLast",
            messages = new[] { message }
        };
        _messageBus.Publish(ChannelHistoryChanged, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteHistoryChanged(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("sessionId").GetString()!;
            var op = root.GetProperty("op").GetString();
            var incoming = JsonSerializer.Deserialize<List<ConversationMessage>>(
                root.GetProperty("messages").GetRawText()) ?? [];
            if (incoming.Count == 0) return;

            // Bu session bu pod'da hiç görülmediyse eklemiyoruz — kısmi/eksik bir liste
            // oluşturmak yerine ilk gerçek erişimde EnsureSessionHydrated'ın DB'den TAM
            // geçmişi çekmesine bırakıyoruz.
            if (!_messageHistory.TryGetValue(sessionId, out var history)) return;

            lock (history)
            {
                if (op == "replaceLast" && history.Count > 0
                    && history[^1].Role == ConversationRoles.Assistant)
                {
                    history[^1] = incoming[0];
                }
                else
                {
                    history.AddRange(incoming);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Session] Redis OnRemoteHistoryChanged parse hatası");
        }
    }

    private void PublishSessionCleared(string sessionId)
    {
        var payload = new { nodeId = _messageBus.NodeId, sessionId };
        _messageBus.Publish(ChannelSessionCleared, JsonSerializer.Serialize(payload));
    }

    private void OnRemoteSessionCleared(string val)
    {
        try
        {
            using var doc = JsonDocument.Parse(val);
            var root = doc.RootElement;
            if (root.GetProperty("nodeId").GetString() == _messageBus.NodeId) return;

            var sessionId = root.GetProperty("sessionId").GetString()!;
            Evict(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Session] Redis OnRemoteSessionCleared parse hatası");
        }
    }
}
