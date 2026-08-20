using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Services.Approval;

/// <summary>
/// AsyncLocal tabanlı IApprovalContextAccessor implementasyonu.
/// Her async akış kendi bağlamını taşır — paralel workflow'lar izoledir.
/// </summary>
public sealed class ApprovalContextAccessor : IApprovalContextAccessor
{
    private static readonly AsyncLocal<ApprovalContext?> _current = new();

    public ApprovalContext? Context => _current.Value;

    public IDisposable SetScope(string? sessionId, string? traceId, string? userQuery, string? customerId = null)
    {
        var previous = _current.Value;
        _current.Value = new ApprovalContext(sessionId, traceId, userQuery, CustomerId: customerId);
        return new ContextScope(previous);
    }

    public void SetCurrentAgent(string? agentName)
    {
        var current = _current.Value;
        _current.Value = current is null
            ? new ApprovalContext(null, null, null, agentName)
            : current with { AgentName = agentName };
    }

    public void SetTraceId(string? traceId)
    {
        var current = _current.Value;
        _current.Value = current is null
            ? new ApprovalContext(null, traceId, null)
            : current with { TraceId = traceId };
    }

    private sealed class ContextScope : IDisposable
    {
        private readonly ApprovalContext? _previous;
        private bool _disposed;

        public ContextScope(ApprovalContext? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = _previous;
        }
    }
}
