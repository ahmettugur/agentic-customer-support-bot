// Ports/Inbound/IComplaintManagementPort.cs
// Panel/API için şikayet yönetimi: liste, sayaç, ayrıntı, durum komutları.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Application.Ports.Inbound;

public enum ComplaintCommandStatus { Updated, NotFound, InvalidTransition, Forbidden, Invalid, Conflict }

/// <param name="Complaint">Güncel kayıt (bulunamadıysa null). 409'da panelin yenilemesi için de döner.</param>
public sealed record ComplaintCommandResult(ComplaintCommandStatus Status, ComplaintInfo? Complaint = null, string? Error = null);

/// <param name="Text">Resolve/Reject: müşteriye görünen açıklama; Reopen/Note: not.</param>
/// <param name="InternalNote">Yalnız Resolve/Reject: olay satırına yazılan iç not.</param>
public sealed record ComplaintCommand(
    ComplaintAction Action,
    string? AssigneeAgentId = null,
    string? Text = null,
    string? InternalNote = null,
    int? ExpectedVersion = null);

public sealed record ComplaintDetail(
    string ComplaintId,
    ComplaintInfo Complaint,
    OrderInfo? Order,
    IReadOnlyList<string> AttachmentIds,
    IReadOnlyList<ComplaintEvent> Events);

public interface IComplaintManagementPort
{
    Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default);

    Task<ComplaintCounts> CountsAsync(string? agentId, CancellationToken ct = default);

    Task<ComplaintDetail?> GetAsync(string complaintId, CancellationToken ct = default);

    Task<ComplaintCommandResult> ExecuteAsync(
        string complaintId, ComplaintActor actor, ComplaintCommand command, CancellationToken ct = default);
}
