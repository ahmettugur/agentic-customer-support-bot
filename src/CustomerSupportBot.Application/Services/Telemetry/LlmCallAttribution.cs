// Application/Services/Telemetry/LlmCallAttribution.cs

using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Application.Services.Telemetry;

/// <summary>AsyncLocal tabanlı <see cref="ILlmCallAttribution"/> (bkz. ApprovalContextAccessor — aynı desen).</summary>
public sealed class LlmCallAttribution : ILlmCallAttribution
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CurrentSessionId => Current.Value;

    public IDisposable BeginSession(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return NoopScope.Instance;
        var previous = Current.Value;
        Current.Value = sessionId;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
