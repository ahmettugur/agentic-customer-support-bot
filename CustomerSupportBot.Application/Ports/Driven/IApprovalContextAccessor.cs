namespace CustomerSupportBot.Application.Ports.Driven;

public interface IApprovalContextAccessor
{
    ApprovalContext? Context { get; }
    IDisposable SetScope(string? sessionId, string? traceId, string? userQuery);
}

public sealed record ApprovalContext(string? SessionId, string? TraceId, string? UserQuery);
