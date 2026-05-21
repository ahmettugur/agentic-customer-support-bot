namespace CustomerSupportBot.Application.Services;

/// <summary>
/// AsyncLocal tabanlı IApprovalContextAccessor implementasyonu.
/// Her async akış kendi bağlamını taşır — paralel workflow'lar izoledir.
/// </summary>
public sealed class ApprovalContextAccessor : IApprovalContextAccessor
{
    private static readonly AsyncLocal<ApprovalContext?> _current = new();

    public ApprovalContext? Context => _current.Value;

    public IDisposable SetScope(string? sessionId, string? traceId, string? userQuery)
    {
        var previous = _current.Value;
        _current.Value = new ApprovalContext(sessionId, traceId, userQuery);
        return new ContextScope(previous);
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
