// Models/StreamEvent.cs
// SSE üzerinden frontend'e gönderilen event tipleri.

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Streaming event modelI. SSE formatında frontend'e gönderilir.
/// Type: event adı (session, reasoning_start, reasoning_delta, reasoning_complete,
///       Agent, response_start, response_delta, response_complete, error, done)
/// Data: JSON olarak serileştirilebilir veri
/// </summary>
public record StreamEvent(string Type, object? Data);

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
    /// Bağlanıyor, bekleyin" sinyali. Payload: { escalationId, reason, createdAt }.
    /// </summary>
    public const string HandoffPending = "handoff_pending";
    /// <summary>
    /// Pending handoff iptal oldu (admin dismiss etti veya takeover olmadan resolve
    /// Edildi). Kullanıcı pending banner'ını kaldırır. Payload: { escalationId, reason }.
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
}

