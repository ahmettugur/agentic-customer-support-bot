namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Approval akışı boyunca taşınan ambient bağlama erişim için secondary (driven) port.
/// Application servisleri bağlamı set eder; HITL adaptörü (ApprovalGateService) okur.
/// </summary>
public interface IApprovalContextAccessor
{
    ApprovalContext? Context { get; }
    IDisposable SetScope(string? sessionId, string? traceId, string? userQuery);
}

public sealed record ApprovalContext(string? SessionId, string? TraceId, string? UserQuery);
