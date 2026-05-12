// Services/IApprovalQueue.cs
// HITL — Approval queue sözleşmesi. Tool lambda'sı CreateAsync ile request
// Yazar, AwaitDecisionAsync ile karar bekler. Admin endpoint'leri Decide
// Çağırır. GetPending/Get sadece UI için.

using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

public interface IApprovalQueue
{
    /// <summary>
    /// Yeni bir approval request oluşturur ve kuyruğa ekler. Workflow tool
    /// Lambda'sı bunu çağırır.
    /// </summary>
    ApprovalRequest Create(ApprovalRequest request);

    /// <summary>
    /// Verilen request için karar verilene kadar bekler. Timeout aşılırsa
    /// Config'teki AutoApproveOnTimeout'a göre otomatik karar verilir.
    /// </summary>
    Task<ApprovalRequest> AwaitDecisionAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Admin kararını işler. Approve/reject → TaskCompletionSource release.
    /// Request Pending değilse false döner (idempotent).
    /// </summary>
    bool Decide(string id, bool approved, string? decidedBy = null, string? reason = null);

    /// <summary>Pending olan tüm istekler (admin UI için).</summary>
    IReadOnlyList<ApprovalRequest> GetPending();

    /// <summary>Son N karar (history).</summary>
    IReadOnlyList<ApprovalRequest> GetRecent(int count = 50);

    /// <summary>Tek bir request'i id ile getir.</summary>
    ApprovalRequest? Get(string id);

    /// <summary>
    /// Event — yeni request oluşunca fire eder. ChatEndpoints SSE için
    /// Subscribe olur.
    /// </summary>
    event EventHandler<ApprovalRequest>? RequestCreated;

    /// <summary>Karar verildiğinde fire eder.</summary>
    event EventHandler<ApprovalRequest>? RequestDecided;
}
