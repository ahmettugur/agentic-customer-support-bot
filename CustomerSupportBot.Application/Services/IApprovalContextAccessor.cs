// Application/Services/IApprovalContextAccessor.cs
// Uygulama-içi ambient context soyutlaması — HITL approval akışı için.
// Implementasyon: ApprovalContextAccessor (AsyncLocal tabanlı, aynı dosya).
// Adapters.Agents/ApprovalGateService bu interface'i tüketir (uygulama-içi bağımlılık).

namespace CustomerSupportBot.Application.Services;

public interface IApprovalContextAccessor
{
    ApprovalContext? Context { get; }
    IDisposable SetScope(string? sessionId, string? traceId, string? userQuery);
}

public sealed record ApprovalContext(string? SessionId, string? TraceId, string? UserQuery);
