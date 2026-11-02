// Application/Ports/Outbound/AI/LiveVoiceModels.cs
// ILiveVoiceTransport port'u için domain-nötr veri modelleri (protokol JSON'u burada yoktur).

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Ports.Outbound.AI;

/// <param name="SessionContext">Oturuma özel talimat: müşteri adı ve tarih. Ses ve arka plan talimatlarının sonuna eklenir.</param>
/// <param name="History">Görüşmenin önceki mesajları; model konuşmaya bağlamıyla başlar.</param>
public sealed record LiveSessionSetup(string? SessionContext, IReadOnlyList<ConversationMessage> History);

public enum LiveServerEventType
{
    SessionStarted,
    /// <summary>Çıkış sesi. Sessiz paketler de gelir; <see cref="LiveServerEvent.HasSpeech"/> konuşma olup olmadığını söyler.</summary>
    AudioDelta,
    InputTranscriptDelta,
    OutputTranscriptDelta,
    /// <summary>Ses modeli işi arka plan modeline devretti.</summary>
    DelegationCreated,
    ToolCallReady,
    BackendResponseCompleted,
    /// <summary>Arka plan yanıtı başarısız ya da eksik bitti (kullanım varsa taşır).</summary>
    BackendResponseFailed,
    /// <summary>Toplam ses süresi; değerler kümülatiftir, toplanmaz.</summary>
    UsageUpdated,
    SessionClosed,
    Error,
    ConnectionClosed
}

public sealed record LiveServerEvent(LiveServerEventType EventType)
{
    public byte[]? AudioDelta { get; init; }
    public bool HasSpeech { get; init; }
    public string? TextDelta { get; init; }
    public string? DelegationId { get; init; }
    public string? ResponseId { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArguments { get; init; }
    public double? UsageSeconds { get; init; }
    public string? BackendModel { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    /// <summary><c>close_requested</c>, <c>expired</c>, <c>content</c>, <c>remote_hangup</c>, <c>connection_lost</c>.</summary>
    public string? CloseReason { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
