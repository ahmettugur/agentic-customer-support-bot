// Services/ApprovalContextAccessor.cs
// Approval context bağlam yönetimi — static AsyncLocal yerine explicit scope.
// IDisposable ile scope sonunda otomatik temizlik sağlar.
// Her async akış kendi bağlamını taşır — paralel workflow'lar izoledir.

namespace CustomerSupportBot.Api.Services;

/// <summary>
/// Workflow çalıştırma sırasında approval bağlamını (session, trace, query)
/// taşıyan erişim noktası.
/// </summary>
public interface IApprovalContextAccessor
{
    /// <summary>Geçerli bağlamı okur.</summary>
    ApprovalContext? Context { get; }

    /// <summary>
    /// Yeni bir bağlam kapsamı oluşturur. Scope dispose edildiğinde bağlam temizlenir.
    /// <c>using</c> bloğu ile kullanılmalıdır.
    /// </summary>
    IDisposable SetScope(string? sessionId, string? traceId, string? userQuery);
}

/// <summary>
/// AsyncLocal tabanlı approval context accessor.
/// Her async akış kendi bağlamını taşır — paralel workflow'lar izoledir.
/// <see cref="IDisposable"/> scope ile yaşam döngüsü açık ve temizlik garantili.
/// Önceki bağlam restore edilir (nested scope desteği).
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

    /// <summary>
    /// Scope dispose edildiğinde önceki bağlamı restore eder.
    /// </summary>
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

/// <summary>HITL bağlam kaydı (session/trace/user query).</summary>
public sealed record ApprovalContext(string? SessionId, string? TraceId, string? UserQuery);
