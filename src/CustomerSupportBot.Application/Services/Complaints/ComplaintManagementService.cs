// Application/Services/Complaints/ComplaintManagementService.cs
// Şikayet yaşam döngüsü: kural (ComplaintTransitions) → koşullu depo geçişi → müşteri bildirimi.

using System.Text.Json;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Notifications;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Application.Services.Complaints;

public sealed class ComplaintManagementService(
    IComplaintRepository complaints,
    IOrderRepository orders,
    IApprovalQueue approvals,
    IHumanAgentRegistry agents,
    ComplaintStatusEmailService notifier,
    ILogger<ComplaintManagementService> logger,
    TimeProvider? clock = null) : IComplaintManagementPort
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<ComplaintPage> ListAsync(ComplaintQuery query, CancellationToken ct = default) =>
        complaints.ListAsync(query, ct);

    public Task<ComplaintCounts> CountsAsync(string? agentId, CancellationToken ct = default) =>
        complaints.CountOpenAsync(agentId, ct);

    public async Task<ComplaintDetail?> GetAsync(string complaintId, CancellationToken ct = default)
    {
        var complaint = complaints.Get(complaintId);
        if (complaint is null) return null;
        var events = await complaints.GetEventsAsync(complaintId, ct);
        var attachmentIds = await AttachmentIdsAsync(complaint.ApprovalId, ct);
        return new ComplaintDetail(complaintId, complaint, orders.Get(complaint.OrderId), attachmentIds, events);
    }

    public async Task<ComplaintCommandResult> ExecuteAsync(
        string complaintId, ComplaintActor actor, ComplaintCommand command, CancellationToken ct = default)
    {
        var complaint = complaints.Get(complaintId);
        if (complaint is null) return new ComplaintCommandResult(ComplaintCommandStatus.NotFound);

        var check = ComplaintTransitions.Check(command.Action, complaint, actor, command.AssigneeAgentId, command.Text);
        if (!check.Allowed)
            return check.Error switch
            {
                ComplaintTransitionErrors.InvalidTransition => new(ComplaintCommandStatus.InvalidTransition, complaint, "Şikayet bu durumdayken bu işlem yapılamaz."),
                ComplaintTransitionErrors.Forbidden => new(ComplaintCommandStatus.Forbidden, complaint, "Bu şikayet size atanmamış."),
                ComplaintTransitionErrors.AssigneeRequired => new(ComplaintCommandStatus.Invalid, complaint, "Bir temsilci seçin."),
                ComplaintTransitionErrors.TooLong => new(ComplaintCommandStatus.Invalid, complaint, $"Metin en fazla {ComplaintTransitions.MaxTextLength} karakter olabilir."),
                _ => new(ComplaintCommandStatus.Invalid, complaint,
                    command.Action is ComplaintAction.Resolve or ComplaintAction.Reject
                        ? "Müşteriye gönderilecek açıklama zorunludur."
                        : "Not zorunludur.")
            };

        if (command.Action == ComplaintAction.Assign && agents.Get(command.AssigneeAgentId!.Trim()) is not { IsActive: true })
            return new ComplaintCommandResult(ComplaintCommandStatus.Invalid, complaint, "Geçerli, aktif bir temsilci seçin.");

        if (command.ExpectedVersion is { } expected && expected != complaint.Version)
            return new ComplaintCommandResult(ComplaintCommandStatus.Conflict, complaint, "Şikayet başka biri tarafından güncellendi.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var text = command.Text?.Trim();

        if (command.Action == ComplaintAction.Note)
        {
            var added = await complaints.AddEventAsync(complaintId,
                new ComplaintEvent(now, actor.Name, ComplaintEventKinds.Note, null, null, null, text), ct);
            return added
                ? new ComplaintCommandResult(ComplaintCommandStatus.Updated, complaint)
                : new ComplaintCommandResult(ComplaintCommandStatus.NotFound);
        }

        var assignee = command.Action switch
        {
            ComplaintAction.Claim => actor.AgentId,
            ComplaintAction.Assign => command.AssigneeAgentId!.Trim(),
            _ => complaint.AssignedAgentId
        };
        var (kind, note, resolution) = command.Action switch
        {
            ComplaintAction.Claim => (ComplaintEventKinds.Claimed, (string?)null, (string?)null),
            ComplaintAction.Assign => (ComplaintEventKinds.Assigned, null, null),
            ComplaintAction.Resolve => (ComplaintEventKinds.Resolved, Blank(command.InternalNote), text),
            ComplaintAction.Reject => (ComplaintEventKinds.Rejected, Blank(command.InternalNote), text),
            _ => (ComplaintEventKinds.Reopened, text, null)
        };

        var outcome = await complaints.TryTransitionAsync(new ComplaintTransition(
            complaintId, complaint.Status, complaint.Version, check.TargetStatus!, assignee, resolution,
            new ComplaintEvent(now, actor.Name, kind, complaint.Status, check.TargetStatus, assignee, note)), ct);

        switch (outcome.Change)
        {
            case ComplaintTransitionChange.NotFound:
                return new ComplaintCommandResult(ComplaintCommandStatus.NotFound);
            case ComplaintTransitionChange.Conflict:
                return new ComplaintCommandResult(ComplaintCommandStatus.Conflict, outcome.Complaint, "Şikayet başka biri tarafından güncellendi.");
        }

        var updated = outcome.Complaint!;
        var emailKind = command.Action switch
        {
            ComplaintAction.Claim or ComplaintAction.Reopen => ComplaintStatusEmailService.InReview,
            ComplaintAction.Assign when complaint.Status == WellKnown.ComplaintStatuses.Pending => ComplaintStatusEmailService.InReview,
            ComplaintAction.Resolve or ComplaintAction.Reject => ComplaintStatusEmailService.Closed,
            _ => null
        };
        if (emailKind is not null) await notifier.NotifyAsync(complaintId, updated, emailKind, ct);

        logger.LogInformation("[Complaint] {Action} complaint={ComplaintId} {From}→{To} by={Actor}",
            ComplaintTransitions.ActionName(command.Action), complaintId, complaint.Status, updated.Status, actor.Name);
        return new ComplaintCommandResult(ComplaintCommandStatus.Updated, updated);
    }

    private async Task<IReadOnlyList<string>> AttachmentIdsAsync(string? approvalId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(approvalId)) return [];
        var approval = await approvals.GetAsync(approvalId, ct);
        if (approval is null
            || !approval.Parameters.TryGetValue(SideEffectApprovalGate.AttachmentIdsParameter, out var raw) || raw is null)
            return [];
        return raw switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } arr => arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList(),
            IEnumerable<string> list => list.ToList(),
            _ => []
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
