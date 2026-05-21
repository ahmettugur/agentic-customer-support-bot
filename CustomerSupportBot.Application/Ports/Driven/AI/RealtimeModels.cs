// Application/Ports/Driven/AI/RealtimeModels.cs
// IRealtimeVoiceTransport port'u için domain-nötr veri modelleri.
// OpenAI protokolüne özgü tipler (JsonNode, base64, event string'leri) burada yoktur.

namespace CustomerSupportBot.Application.Ports.Driven.AI;

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
}

public enum RealtimeServerEventType
{
    SpeechStarted,
    SpeechStopped,
    InputTranscriptCompleted,
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
