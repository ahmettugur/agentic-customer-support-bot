// Application/Ports/Driven/AI/RealtimeModels.cs
// IRealtimeVoiceTransport port'u için domain-nötr veri modelleri.
// OpenAI protokolüne özgü tipler (JsonNode, base64, event string'leri) burada yoktur.

namespace CustomerSupportBot.Application.Ports.Outbound.AI;

public sealed record RealtimeToolResult(string CallId, string Name, string OutputJson);

public sealed record RealtimeServerEvent(RealtimeServerEventType EventType)
{
    public string? Transcript   { get; init; }
    public byte[]? AudioDelta   { get; init; }
    public string? TextDelta    { get; init; }
    public string? FullText     { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ToolCallId   { get; init; }
    public string? ToolName     { get; init; }
    public string? ToolArguments { get; init; }

    /// <summary>
    /// Olayın ait olduğu kullanıcı ses öğesinin kimliği (<see cref="RealtimeServerEventType.SpeechStarted"/>,
    /// <see cref="RealtimeServerEventType.SpeechStopped"/>, <see cref="RealtimeServerEventType.InputAudioCommitted"/>,
    /// <see cref="RealtimeServerEventType.InputTranscriptDelta"/>,
    /// <see cref="RealtimeServerEventType.InputTranscriptCompleted"/>,
    /// <see cref="RealtimeServerEventType.InputTranscriptFailed"/>). Transkript yanıt olaylarından
    /// önce de sonra da gelebildiği için turu eşleştirmenin tek güvenilir anahtarı budur.
    /// </summary>
    public string? ItemId { get; init; }
}

public enum RealtimeServerEventType
{
    SpeechStarted,
    SpeechStopped,
    /// <summary>Kullanıcı sesi bir konuşma öğesi olarak işlendi (<c>ItemId</c> taşır).</summary>
    InputAudioCommitted,
    InputTranscriptCompleted,
    /// <summary>
    /// Kullanıcı konuşmasının anlık transkript parçası (<c>ItemId</c> + <c>TextDelta</c>). Yalnızca
    /// ekranda canlı altyazı içindir; parçaların birleşimi son metne eşit olmak zorunda değildir —
    /// kalıcı kayıt her zaman <see cref="InputTranscriptCompleted"/>'tan yapılır.
    /// </summary>
    InputTranscriptDelta,
    /// <summary>Kullanıcı sesinin transkripsiyonu başarısız oldu (<c>ItemId</c> taşır).</summary>
    InputTranscriptFailed,
    ResponseCreated,
    AudioDelta,
    AssistantTextDelta,
    AssistantTextDone,
    ToolCallReady,
    ResponseDone,
    ResponseCancelled,
    Error,
    ConnectionClosed
}
