// Ports/Driving/StreamEvent.cs
// IChatPort streaming use case output DTO'su ve event tipleri.

using System.Text.Json.Serialization;

namespace CustomerSupportBot.Application.Ports.Inbound;

/// <summary>
/// Streaming event modeli — use case boundary output.
/// Type: event adı, Data: JSON olarak serileştirilebilir veri.
/// </summary>
public record StreamEvent(string Type, object? Data);

/// <summary>
/// Session event payload — SessionId contract'ını typed tutar.
/// ChatPortService ve ChatEndpoints bu tipi kullanarak sessiz failure riskini ortadan kaldırır.
/// </summary>
public sealed record SessionEventPayload(string SessionId);

/// <summary>
/// <see cref="StreamEventTypes.ResponseDelta"/> ve <see cref="StreamEventTypes.ReasoningDelta"/>
/// event'lerinin payload'ı. Önceden anonim <c>new { text = ... }</c> nesneleri kullanılıyordu ve
/// aggregator'lar (ChatPortService, RealtimeBridgeService, WorkflowResponseExtractor) bunu JSON
/// round-trip veya reflection ile okumak zorunda kalıyordu — rename'de sessizce boş string
/// dönerlerdi. Bu record aynı JSON şekli (camelCase → "text") üretir, tipli okumaya izin verir.
/// </summary>
public sealed record TextDeltaPayload(string Text);

/// <summary>
/// <see cref="StreamEventTypes.ResponseComplete"/> payload'ı — turun <b>kanonik</b> yanıt metni.
///
/// <para>
/// <b>Neden tipli olması şart:</b> bu event'in taşıdığı metin, delta akışının taşıdığından
/// FARKLI olabilir. Delta'lar ResponseAgent'ın ham token akışıdır (yalnızca TERMINATE'ten
/// kesilir); buradaki metin ise ek temizlikten geçmiştir — teknik JSON blokları silinir ve
/// yanıtta ajan adı sızıntısı varsa (<c>ContainsAgentRoutingMessage</c>) metin LLM ile
/// <b>tamamen yeniden yazılır</b>.
/// </para>
///
/// <para>
/// 🐞 <b>Bu tip bir hatayı kapatmak için eklendi.</b> Payload eskiden anonim bir
/// <c>new { text = ... }</c> nesnesiydi ve sunucu tarafında <b>hiçbir tüketicisi yoktu</b> —
/// tipli okunamadığı için kimse okumaya çalışmamıştı. Sonuç: <c>ChatPortService</c> konuşma
/// geçmişini, <c>RealtimeBridgeService</c> ise TTS'e okutulacak metni delta'ları birleştirerek
/// üretiyordu. Ajan adı sızıntısı olan bir turda ekranda temiz metin görünürken
/// <b>veritabanına ham metin yazılıyor</b> ve <b>sesli kanalda müşteri "OrderAgent size
/// yardımcı olacak" gibi bir cümleyi duyuyordu</b> — yani <c>RewriteRoutingMessageAsync</c>
/// savunması yalnızca yazılı sohbet ekranı için çalışıyor, kalıcılığı ve sesi baypas ediyordu.
/// </para>
///
/// <para>
/// Tüketiciler artık bu metni kanonik kaynak olarak kullanır ve event hiç gelmezse
/// (hata/iptal) delta birleşimine geri düşer.
/// </para>
/// </summary>
/// <param name="Text">Kullanıcıya gösterilen/kaydedilen nihai metin.</param>
/// <param name="TerminationReason">Turun sonlanma sebebi (<c>WellKnown.Termination</c>).</param>
/// <param name="Revised">Yanıt self-critique sonrası revize edildiyse true.</param>
/// <param name="Decomposed">Compound sorgu alt görevlere bölündüyse true.</param>
/// <param name="SubTaskCount">Bölünme varsa alt görev sayısı.</param>
public sealed record ResponseCompletePayload(
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TerminationReason = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Revised = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Decomposed = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SubTaskCount = null);

/// <summary>Tanımlı event tipleri — tip güvenliği için sabitler.</summary>
public static class StreamEventTypes
{
    public const string Session = "session";
    public const string ReasoningStart = "reasoning_start";
    public const string ReasoningDelta = "reasoning_delta";
    public const string ReasoningComplete = "reasoning_complete";
    public const string Agent = "agent";
    public const string ResponseStart = "response_start";
    public const string ResponseDelta = "response_delta";
    public const string ResponseComplete = "response_complete";
    public const string Error = "error";
    public const string Done = "done";

    // ─── HITL events ───
    /// <summary>Bir tool çağrısı admin onayı bekliyor. Payload: ApprovalRequest.</summary>
    public const string ApprovalRequired = "approval_required";
    /// <summary>Onay kararı verildi. Payload: { id, status, reason? }.</summary>
    public const string ApprovalResolved = "approval_resolved";
    /// <summary>Yeni bir eskalasyon kaydı oluştu. Payload: EscalationRequest.</summary>
    public const string EscalationCreated = "escalation_created";

    // ─── HITL Live Takeover events ───
    /// <summary>İnsan temsilci session'ı devraldı. Payload: { humanAgent, sessionId, enteredAt }.</summary>
    public const string HumanJoined = "human_joined";
    /// <summary>Admin/system tarafından user'a mesaj. Payload: { from, humanAgent?, text, timestamp }.</summary>
    public const string HumanMessage = "human_message";
    /// <summary>Human mod bitti; session Bot'a döndü. Payload: { sessionId }.</summary>
    public const string HumanLeft = "human_left";

    /// <summary>
    /// Bu session için bir handoff/eskalasyon açıldı; kullanıcıya "bir temsilci
    /// bağlanıyor, bekleyin" sinyali. Payload: { escalationId, reason, createdAt }.
    /// </summary>
    public const string HandoffPending = "handoff_pending";
    /// <summary>
    /// Pending handoff iptal oldu (admin dismiss etti veya takeover olmadan resolve
    /// edildi). Kullanıcı pending banner'ını kaldırır. Payload: { escalationId, reason }.
    /// </summary>
    public const string HandoffCleared = "handoff_cleared";

    /// <summary>Admin SSE — bridge üzerinden user mesajı admin paneline iletilir.</summary>
    public const string BridgeMessage = "bridge_message";

    /// <summary>
    /// Bot arka planda otomatik yanıt hazırlıyor (ör. admin replan'ı sonrası).
    /// Payload: { on: bool }. Frontend typing indicator + input kilidini yönetir.
    /// </summary>
    public const string BotTyping = "bot_typing";

    // ─── Sentiment events ───
    /// <summary>Her tur sonrası duygu güncellemesi. Payload: { sentiment, score, consecutive }.</summary>
    public const string SentimentUpdate = "sentiment_update";
    /// <summary>Duygu skoru kritik eşiğin altına düştü. Payload: { sentiment, score, consecutive, sessionId }.</summary>
    public const string SentimentAlert = "sentiment_alert";

    // ─── UI hint events ───
    /// <summary>
    /// Tool çıktısına bağlı frontend UI bileşeni. Payload: { kind, ...data }.
    /// kind="category_picker" → { categories: string[] }
    /// </summary>
    public const string UiHint = "ui_hint";
}
