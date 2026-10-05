using System.Globalization;
using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Attachments;
using CustomerSupportBot.Application.Services.Budget;
using CustomerSupportBot.Domain.Exceptions;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// IChatPort implementasyonu — chat kullanım senaryosunu orkestre eder.
///Human mode (HITL), sentiment güncelleme ve persist işlemleri burada yönetilir.
/// </summary>
public sealed class ChatPortService : IChatPort
{
    private readonly IAgentTeamPort _team;
    private readonly IReasoningPort _reasoning;
    private readonly ISessionManager _sessions;
    private readonly IChatModeRegistry _modeRepo;
    private readonly IChatBridge _chatBridge;
    private readonly SessionStateService _sessionState;
    private readonly IApprovalContextAccessor _approvalContext;
    private readonly IAppDistributedLock _turnLock;
    private readonly ILogger<ChatPortService> _logger;
    private readonly IAttachmentStore? _attachments;
    private readonly AttachmentOptions _attachmentOptions;
    private readonly Ports.Outbound.Observability.ILlmCallAttribution? _attribution;
    private readonly ILlmSpendGuard? _spendGuard;
    private readonly IEscalationSink? _escalations;
    private readonly LlmBudgetOptions _budget;

    /// <summary>
    /// Bir turun kilidi bekleyebileceği azami süre. Bir tur LLM çağrıları yüzünden onlarca
    /// saniye sürebilir; ikinci mesaj REDDEDİLMEK yerine SIRAYA girmelidir, çünkü amaç
    /// sıralamayı korumaktır. Medallion kilidi tutulduğu sürece kendini yeniler, dolayısıyla
    /// uzun turlarda kilit düşmez.
    /// </summary>
    private static readonly TimeSpan TurnLockWait = TimeSpan.FromSeconds(120);

    public ChatPortService(
        IAgentTeamPort team,
        IReasoningPort reasoning,
        ISessionManager sessions,
        IChatModeRegistry modeRepo,
        IChatBridge chatBridge,
        SessionStateService sessionState,
        IApprovalContextAccessor approvalContext,
        IAppDistributedLock turnLock,
        ILogger<ChatPortService> logger,
        IAttachmentStore? attachments = null,
        IOptions<AttachmentOptions>? attachmentOptions = null,
        Ports.Outbound.Observability.ILlmCallAttribution? attribution = null,
        ILlmSpendGuard? spendGuard = null,
        IEscalationSink? escalations = null,
        IOptions<LlmBudgetOptions>? budgetOptions = null)
    {
        _attribution = attribution;
        _spendGuard = spendGuard;
        _escalations = escalations;
        _budget = budgetOptions?.Value ?? new LlmBudgetOptions();
        _team = team;
        _reasoning = reasoning;
        _sessions = sessions;
        _modeRepo = modeRepo;
        _chatBridge = chatBridge;
        _sessionState = sessionState;
        _approvalContext = approvalContext;
        _turnLock = turnLock;
        _logger = logger;
        _attachments = attachments;
        _attachmentOptions = attachmentOptions?.Value ?? new AttachmentOptions();
    }

    /// <summary>
    /// Aynı oturumdaki turları SIRAYA sokar.
    ///
    /// <para>
    /// Bir tur "geçmişi oku → akıl yürüt → workflow → geçmişe yaz" adımlarından oluşur ve bu
    /// dizi atomik değildi: aynı anda gelen iki mesaj AYNI eski geçmişi okuyup sonuçlarını
    /// bitiş sırasına göre yazabiliyordu. Sonuç, nedensel bağlamın kaybı (ikinci mesaj
    /// birincinin yanıtını görmez) ve <c>ForceReplanNextTurn</c> gibi tek kullanımlık
    /// durumların tutarsız tüketilmesidir.
    /// </para>
    ///
    /// <para>
    /// Anahtar <see cref="SessionIdentityBinder.TurnLockKey"/>'den gelir — realtime kanalları
    /// da kimlik bağlarken AYNI anahtarı kullanır, böylece yazılı tur ile sesli bağlantı
    /// birbirini dışlar. (<c>session:{id}</c> kullanılamaz: onu tur İÇİNDE
    /// <c>MutateStateAsync</c>/<c>AddExchangeAsync</c> alıyor ve Redis kilidi yeniden girişli
    /// olmadığı için kendi kendine kilitlenme üretirdi.)
    /// </para>
    /// </summary>
    private async Task<IAsyncDisposable> AcquireTurnLockAsync(string sessionId, CancellationToken ct)
    {
        var handle = await _turnLock.TryAcquireAsync(
            SessionIdentityBinder.TurnLockKey(sessionId), TurnLockWait, ct);
        if (handle is not null) return handle;

        _logger.LogWarning(
            "Oturum turu kilidi {Wait}s içinde alınamadı (sessionId={SessionId}) — "
          + "aynı oturumda hâlâ süren bir tur var.", TurnLockWait.TotalSeconds, sessionId);

        throw new InvalidOperationException(
            "Bu oturumda hâlâ işlenen bir mesaj var. Lütfen yanıtı bekleyip tekrar deneyin.");
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> HandleAsync(ChatRequest request, CancellationToken ct = default)
    {
        var query = request.Query;
        var session = await _sessions.GetOrCreateAsync(request.SessionId, ct);
        var sessionId = session.SessionId;
        // Görüşme başına maliyet: bu turdaki tüm LLM çağrıları (akıl yürütme + ajanlar) bu görüşmeye atfedilir.
        using var costScope = _attribution?.BeginSession(sessionId);
        // Kilit BİNDDEN ÖNCE alınır. Bind bir "oku-karar ver-yaz" dizisidir: oturum henüz
        // kimseye bağlı değilse çağıranı bağlar. Kilidin dışında kalırsa, sahipsiz aynı
        // oturuma eşzamanlı gelen iki farklı müşteri de "bağlı değil" görüp ikisi de
        // bağlamayı deneyebilir — sahiplik yarışı. Kilit ayrıca "oku → yaz" turunun
        // tamamını kapsamalı, o yüzden geçmiş okunmadan önce de alınmış olur.
        await using var turnLock = await AcquireTurnLockAsync(sessionId, ct);

        // Kilit altında TAZELE. Yukarıdaki nesne kilidi beklemeye başlamadan önce alındı;
        // bu arada başka bir pod oturumu güncellediyse Redis dinleyicisi cache'e YENİ bir
        // nesne koyar (mevcut olanı değiştirmez), yani elimizdeki referans sessizce eskir.
        // Bind bir "oku-karar ver-yaz" adımı olduğu için bayat okuma iki pod'un aynı sahipsiz
        // oturumu birbirinden habersiz bağlamasına yol açabilirdi.
        session = await _sessions.ReloadAsync(sessionId, ct);

        await BindAuthenticatedCustomerAsync(session, request.CustomerId, ct);
        query = await AppendAttachmentsAsync(query, request, sessionId, ct);

        if (_modeRepo.GetMode(sessionId) == ChatMode.Human)
        {
            await ForwardHumanMessageAsync(sessionId, query, ct);
            return new ChatResponse("Mesajınız müşteri temsilcisine iletildi.", sessionId);
        }

        if (await BudgetReplyAsync(sessionId, query, ct) is { } budgetReply)
        {
            await _sessions.AddExchangeAsync(sessionId, query, budgetReply, ct: ct);
            return new ChatResponse(budgetReply, sessionId);
        }

        var history = await _sessions.GetHistoryAsync(sessionId, ct);

        var reasoningResult = await _reasoning.ReasonAsync(query, session, history, ct);

        using var approvalScope = _approvalContext.SetScope(sessionId, null, query, session.State.AuthenticatedCustomerId);
        string response;
        try
        {
            response = await _team.RunAsync(query, history, session, reasoningResult, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hata/timeout'ta kullanıcının mesajı yine geçmişe yazılır — streaming yolla aynı
            // davranış (bkz. SessionStateService.PersistExchangeAsync). Çağıranın iptali
            // (kullanıcı vazgeçti) bu dala girmez.
            await _sessions.AppendUserMessageAsync(sessionId, query, ct);
            throw;
        }

        // Intent/sentiment turun kapanışında TEK yerde işlenir — bkz. TurnSignals.
        // (Bu yol eskiden sentiment'i hiç iletmiyordu; streaming yolla arasındaki
        // davranış farkı da böylece kapanıyor.)
        await _sessions.AddExchangeAsync(sessionId, query, response, TurnSignals.From(reasoningResult), ct);

        return new ChatResponse(response, sessionId, reasoningResult);
    }

    /// <summary>
    /// Login'li müşterinin JWT'den doğrulanmış kimliğini session'a bir kez bağlar ve oturum
    /// sahipliğini doğrular.
    ///
    /// <para>
    /// Eskiden burada yalnızca bağlama vardı: oturum zaten <b>başka</b> bir müşteriye bağlıysa
    /// metot sessizce çıkıyor, tur o oturumun kimliğiyle devam ediyordu. Yani müşteri B,
    /// müşteri A'nın <c>sessionId</c>'sini göndererek A'nın konuşma geçmişini alabiliyor ve
    /// tool'ları A adına çalıştırabiliyordu. Artık ihlalde
    /// <see cref="UnauthorizedSessionAccessException"/> fırlatılır.
    /// </para>
    ///
    /// <para>
    /// Kural <see cref="SessionIdentityBinder"/>'da tek noktada durur — aynı kontrol sesli
    /// kanallarda da gerekiyor ve kopyalandığında biri güncellenip diğerleri kalıyordu.
    /// </para>
    /// </summary>
    private async Task BindAuthenticatedCustomerAsync(AgentSession session, string? customerId, CancellationToken ct)
    {
        if (!await SessionIdentityBinder.TryBindAsync(session, customerId, _sessions, ct))
            throw new UnauthorizedSessionAccessException(session.SessionId);
    }

    /// <summary>
    /// Akan bir alt adımı (akıl yürütme, ajan takımı) görüşmenin maliyet kapsamında numaralandırır.
    ///
    /// <para>
    /// Async iterator içinde AsyncLocal'a verilen değer <c>yield</c>'i aşmaz: her sonraki öğe isteği
    /// TÜKETİCİNİN bağlamında çalışır. Bu metot ilk <c>yield</c>'den (oturum olayı) sonra çalıştığı için
    /// tepedeki kapsam alt adımlara ulaşmıyordu. Burada kapsam her <c>MoveNextAsync</c> çağrısının
    /// etrafında yeniden açılır; alt adımın o çağrıda yaptığı (ve başlattığı) LLM çağrıları görüşmeye atfedilir.
    /// </para>
    /// </summary>
    private async IAsyncEnumerable<T> WithCostScope<T>(
        IAsyncEnumerable<T> source, string sessionId, [EnumeratorCancellation] CancellationToken ct)
    {
        if (_attribution is null)
        {
            await foreach (var item in source.WithCancellation(ct)) yield return item;
            yield break;
        }

        await using var e = source.GetAsyncEnumerator(ct);
        while (true)
        {
            bool hasNext;
            using (_attribution.BeginSession(sessionId)) hasNext = await e.MoveNextAsync();
            if (!hasNext) yield break;
            yield return e.Current;
        }
    }

    /// <summary>
    /// Mesaja eklenen fotoğrafların analizini sorguya ekler (bkz. <see cref="AttachmentTurnContext"/>).
    /// Bağlamadan SONRA çağrılır: sahiplik kontrolü oturumun doğrulanmış müşterisine göre yapılır.
    /// Temsilci modunda da uygulanır — temsilci de fotoğraf notunu görür.
    /// </summary>
    private async Task<string> AppendAttachmentsAsync(string query, ChatRequest request, string sessionId, CancellationToken ct)
    {
        if (_attachments is null || request.AttachmentIds is not { Count: > 0 } ids) return query;

        var resolved = await AttachmentTurnContext.ResolveAsync(
            _attachments, ids, sessionId, request.CustomerId, _attachmentOptions.MaxPerMessage, ct);
        if (resolved.Count < ids.Distinct(StringComparer.Ordinal).Count())
            _logger.LogWarning(
                "Session {SessionId}: {Ignored} attachment id(s) ignored (unknown, foreign or over the per-message limit)",
                sessionId, ids.Distinct(StringComparer.Ordinal).Count() - resolved.Count);

        // Onay kapısı yalnızca gönderilmiş fotoğrafları bağlar; işaret turdan (ve olası onaydan) önce.
        if (resolved.Count > 0)
            await _attachments.MarkSentAsync(resolved.Select(a => a.Id).ToList(), DateTime.UtcNow, ct);

        return AttachmentTurnContext.Compose(query, resolved);
    }

    /// <summary>
    /// Tur başında harcama limiti: aşıldıysa müşteriye verilecek sabit yanıt, değilse <c>null</c>.
    ///
    /// <para>
    /// Günlük/aylık limitte eskalasyon açılmaz — o anda gelen her görüşme için açılsaydı temsilci kuyruğu
    /// dolardı. Görüşme başına limit ise tek bir görüşmeye özgüdür (kaçak döngü ya da kötüye kullanım): oturum
    /// için açık eskalasyon yoksa bir tane açılır, görüşme insan gözüne gelir.
    /// </para>
    /// </summary>
    private async Task<string?> BudgetReplyAsync(string sessionId, string query, CancellationToken ct)
    {
        if (_spendGuard is null) return null;
        var exceeded = await _spendGuard.CheckAsync(sessionId, ct);
        if (exceeded is null) return null;

        _logger.LogWarning(
            "[Budget] LLM bütçesi aşıldı, tur LLM'siz yanıtlandı | session={SessionId} scope={Scope} spent={Spent} limit={Limit}",
            sessionId, exceeded.Scope, exceeded.SpentUsd, exceeded.LimitUsd);
        if (exceeded.Scope != LlmBudgetScope.Conversation) return _budget.UnavailableMessage;

        if (_escalations is not null && !_escalations.GetOpen().Any(e => e.SessionId == sessionId))
        {
            await _escalations.CreateAsync(new EscalationRequest
            {
                SessionId = sessionId,
                AgentName = "LlmBudget",
                UserQuery = query,
                Reason = string.Create(CultureInfo.InvariantCulture,
                    $"Görüşme başına LLM bütçesi aşıldı ({exceeded.SpentUsd:F2} / {exceeded.LimitUsd:F2} USD)")
            });
        }
        return _budget.ConversationLimitMessage;
    }

    private async Task ForwardHumanMessageAsync(string sessionId, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        await _sessions.AppendUserMessageAsync(sessionId, query, ct);
        await _chatBridge.PublishUserMessageAsync(sessionId, query);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = request.Query;
        var session = await _sessions.GetOrCreateAsync(request.SessionId, ct);
        var sessionId = session.SessionId;
        // Görüşme başına maliyet: bu turdaki tüm LLM çağrıları (akıl yürütme + ajanlar) bu görüşmeye atfedilir.
        using var costScope = _attribution?.BeginSession(sessionId);

        // Kilit BİNDDEN ÖNCE (gerekçe için bkz. HandleAsync). Human-mode dalı da kilit
        // altındadır: orada bot turu yok ama geçmişe yazma var, dolayısıyla sıraya girmesi
        // doğrudur.
        await using var turnLock = await AcquireTurnLockAsync(sessionId, ct);

        // Kilit altında tazele — gerekçe için bkz. HandleAsync.
        session = await _sessions.ReloadAsync(sessionId, ct);

        await BindAuthenticatedCustomerAsync(session, request.CustomerId, ct);
        query = await AppendAttachmentsAsync(query, request, sessionId, ct);

        yield return new StreamEvent(StreamEventTypes.Session, new SessionEventPayload(sessionId));

        // Human mode (HITL live takeover) — bot bypass
        if (_modeRepo.GetMode(sessionId) == ChatMode.Human)
        {
            var state = _modeRepo.GetState(sessionId);
            yield return new StreamEvent(StreamEventTypes.HumanJoined, new
            {
                sessionId,
                humanAgent = state?.HumanAgent ?? WellKnown.Defaults.Admin,
                enteredAt = state?.EnteredAt
            });
            if (!string.IsNullOrWhiteSpace(query))
            {
                // Yalnızca kullanıcı mesajı yazılır. AddExchangeAsync burada BOŞ bir asistan
                // mesajı da bırakıyordu: insan modunda bota ait bir yanıt yoktur, temsilcinin
                // cevabı geldiğinde ayrıca yazılır. Boş placeholder geçmişte doldurulmadan
                // kalıyor ve bot oturumu geri devraldığında bağlamı bozuyordu.
                await ForwardHumanMessageAsync(sessionId, query, ct);
            }
            yield break;
        }

        // Harcama limiti aşıldıysa LLM'e gidilmez; sabit metin normal bir yanıt gibi akar.
        if (await BudgetReplyAsync(sessionId, query, ct) is { } budgetReply)
        {
            yield return new StreamEvent(StreamEventTypes.ResponseStart, new { terminationReason = LlmBudgetOptions.TerminationReason });
            yield return new StreamEvent(StreamEventTypes.ResponseDelta, new TextDeltaPayload(budgetReply));
            yield return new StreamEvent(StreamEventTypes.ResponseComplete,
                new ResponseCompletePayload(budgetReply, TerminationReason: LlmBudgetOptions.TerminationReason));
            await _sessionState.PersistExchangeAsync(sessionId, query, budgetReply, _chatBridge, null, ct);
            yield break;
        }

        var history = await _sessions.GetHistoryAsync(sessionId, ct);

        // Reasoning stream
        ReasoningResult? reasoningResult = null;
        await foreach (var evt in WithCostScope(_reasoning.ReasonStreamingAsync(query, session, history, ct), sessionId, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ReasoningComplete && evt.Data is ReasoningResult rr)
            {
                reasoningResult = rr;
            }
        }

        using var approvalScope = _approvalContext.SetScope(sessionId, null, query, session.State.AuthenticatedCustomerId);

        // Workflow stream
        //
        // Geçmişe yazılacak metnin kaynağı: ÖNCE response_complete'in kanonik metni, o hiç
        // gelmezse (hata/iptal) delta birleşimi. Bu ayrım şart, çünkü ikisi farklı olabilir —
        // delta'lar ResponseAgent'ın ham akışı, kanonik metin ise teknik JSON'u temizlenmiş ve
        // gerekiyorsa ajan-adı sızıntısına karşı yeniden yazılmış hâli. Eskiden yalnızca
        // delta'lar birleştirildiği için geçmişe ham metin yazılıyordu (bkz. ResponseCompletePayload).
        var responseBuilder = new System.Text.StringBuilder();
        string? canonicalResponse = null;
        await foreach (var evt in WithCostScope(_team.RunStreamingAsync(query, history, session, reasoningResult, ct), sessionId, ct))
        {
            yield return evt;
            if (evt.Type == StreamEventTypes.ResponseDelta && evt.Data is TextDeltaPayload { Text.Length: > 0 } delta)
            {
                responseBuilder.Append(delta.Text);
            }
            else if (evt.Type == StreamEventTypes.ResponseComplete && evt.Data is ResponseCompletePayload complete)
            {
                canonicalResponse = complete.Text;
            }
        }

        var fullResponse = (canonicalResponse ?? responseBuilder.ToString()).TrimEnd();
        await _sessionState.PersistExchangeAsync(
            sessionId, query, fullResponse, _chatBridge, TurnSignals.From(reasoningResult), ct);

        // Sentiment events
        //
        // Durum, tur BAŞINDA alınan referanstan değil cache'teki güncel nesneden okunur:
        // PersistExchangeAsync duygu alanlarını oturum yöneticisinin elindeki nesneye yazar;
        // tur sırasında oturum cache'ten çıkarılıp yeniden yüklendiyse elimizdeki referans
        // bu turun güncellemesini görmez ve uyarı bir tur geriden gelirdi.
        var alert = _sessionState.CheckSentimentAlert(
            await _sessions.GetAsync(sessionId, ct) ?? session);
        yield return new StreamEvent(StreamEventTypes.SentimentUpdate, new
        {
            sentiment = alert.Sentiment,
            score = alert.Score,
            consecutive = alert.ConsecutiveNegativeTurns,
            sessionId = alert.SessionId
        });
        if (alert.ShouldAlert)
        {
            yield return new StreamEvent(StreamEventTypes.SentimentAlert, new
            {
                sentiment = alert.Sentiment,
                score = alert.Score,
                consecutive = alert.ConsecutiveNegativeTurns,
                sessionId = alert.SessionId,
                message = alert.AlertMessage
            });
        }
    }

}
