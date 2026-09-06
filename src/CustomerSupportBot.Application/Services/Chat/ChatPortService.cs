using System.Runtime.CompilerServices;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Exceptions;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging;

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
        ILogger<ChatPortService> logger)
    {
        _team = team;
        _reasoning = reasoning;
        _sessions = sessions;
        _modeRepo = modeRepo;
        _chatBridge = chatBridge;
        _sessionState = sessionState;
        _approvalContext = approvalContext;
        _turnLock = turnLock;
        _logger = logger;
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

        if (_modeRepo.GetMode(sessionId) == ChatMode.Human)
        {
            await ForwardHumanMessageAsync(sessionId, query, ct);
            return new ChatResponse("Mesajınız müşteri temsilcisine iletildi.", sessionId);
        }

        var history = await _sessions.GetHistoryAsync(sessionId, ct);

        var reasoningResult = await _reasoning.ReasonAsync(query, session, history, ct);

        using var approvalScope = _approvalContext.SetScope(sessionId, null, query, session.State.AuthenticatedCustomerId);
        var response = await _team.RunAsync(query, history, session, reasoningResult, ct);

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

    private async Task ForwardHumanMessageAsync(string sessionId, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        await _sessions.AppendUserMessageAsync(sessionId, query, ct);
        _chatBridge.PublishUserMessage(sessionId, query);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = request.Query;
        var session = await _sessions.GetOrCreateAsync(request.SessionId, ct);
        var sessionId = session.SessionId;

        // Kilit BİNDDEN ÖNCE (gerekçe için bkz. HandleAsync). Human-mode dalı da kilit
        // altındadır: orada bot turu yok ama geçmişe yazma var, dolayısıyla sıraya girmesi
        // doğrudur.
        await using var turnLock = await AcquireTurnLockAsync(sessionId, ct);

        // Kilit altında tazele — gerekçe için bkz. HandleAsync.
        session = await _sessions.ReloadAsync(sessionId, ct);

        await BindAuthenticatedCustomerAsync(session, request.CustomerId, ct);

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

        var history = await _sessions.GetHistoryAsync(sessionId, ct);

        // Reasoning stream
        ReasoningResult? reasoningResult = null;
        await foreach (var evt in _reasoning.ReasonStreamingAsync(query, session, history, ct))
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
        await foreach (var evt in _team.RunStreamingAsync(query, history, session, reasoningResult, ct))
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
        var alert = _sessionState.CheckSentimentAlert(session);
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
