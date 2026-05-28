// Ports/Driving/StreamEvent.cs
// IChatPort streaming use case output DTO'su ve event tipleri.

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
