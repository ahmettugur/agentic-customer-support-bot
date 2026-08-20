namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Approval akışı boyunca taşınan ambient bağlama erişim için secondary (driven) port.
/// Application servisleri bağlamı set eder; HITL adaptörü (ApprovalGateService) okur.
/// </summary>
public interface IApprovalContextAccessor
{
    ApprovalContext? Context { get; }
    IDisposable SetScope(string? sessionId, string? traceId, string? userQuery, string? customerId = null);

    /// <summary>
    /// Mevcut ambient bağlamda şu an fiilen çalışan uzman ajanın adını günceller
    /// (ör. "ProductAgent"). Tool çağrıları (ör. IUiHintEmitter.Emit) bu değeri
    /// okuyarak ürettikleri event'i doğru ajana etiketler — stream event
    /// zamanlamasına/sırasına bağlı kalmadan.
    /// </summary>
    void SetCurrentAgent(string? agentName);

    /// <summary>
    /// Workflow trace oluşturulduktan sonra mevcut ambient scope'a trace kimliğini bağlar.
    /// Onay kayıtları bu değeri audit korelasyonu için kullanır.
    /// </summary>
    void SetTraceId(string? traceId);
}

public sealed record ApprovalContext(
    string? SessionId, string? TraceId, string? UserQuery, string? AgentName = null, string? CustomerId = null);
