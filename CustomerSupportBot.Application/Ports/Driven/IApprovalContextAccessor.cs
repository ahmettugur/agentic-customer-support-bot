// Application/Ports/Driven/IApprovalContextAccessor.cs
// SECONDARY PORT — approval akışının bağlamsal verilerini yönetir.
// Adaptör bağımlılığı: Adapters.Agents/ApprovalGateService bu port'u tüketir.
// Implementasyon: Application/Services/ApprovalContextAccessor (AsyncLocal tabanlı).

namespace CustomerSupportBot.Application.Ports.Driven;

public interface IApprovalContextAccessor
{
    ApprovalContext? Context { get; }
    IDisposable SetScope(string? sessionId, string? traceId, string? userQuery);
}

public sealed record ApprovalContext(string? SessionId, string? TraceId, string? UserQuery);
