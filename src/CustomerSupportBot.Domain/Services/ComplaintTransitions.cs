// Domain/Services/ComplaintTransitions.cs
// Şikayet durum geçişleri — saf kurallar. Sıra: durum → yetki → girdi.

using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

public enum ComplaintAction { Claim, Assign, Resolve, Reject, Reopen, Note }

public enum ComplaintActorRole { Admin, Agent }

/// <param name="AgentId">Temsilcinin <c>HumanAgent.Id</c>'si; admin için null.</param>
/// <param name="Name">Olay kaydına yazılan kullanıcı adı.</param>
public sealed record ComplaintActor(ComplaintActorRole Role, string? AgentId, string Name)
{
    public bool IsAdmin => Role == ComplaintActorRole.Admin;
}

public static class ComplaintTransitionErrors
{
    public const string InvalidTransition = "invalid_transition";
    public const string Forbidden = "forbidden";
    public const string TextRequired = "text_required";
    public const string TooLong = "too_long";
    public const string AssigneeRequired = "assignee_required";
}

public sealed record ComplaintTransitionCheck(bool Allowed, string? TargetStatus, string? Error)
{
    public static ComplaintTransitionCheck Ok(string target) => new(true, target, null);
    public static ComplaintTransitionCheck Fail(string error) => new(false, null, error);
}

public static class ComplaintTransitions
{
    public const int MaxTextLength = 2000;

    private static readonly ComplaintAction[] AllActions =
        [ComplaintAction.Claim, ComplaintAction.Assign, ComplaintAction.Resolve, ComplaintAction.Reject, ComplaintAction.Reopen, ComplaintAction.Note];

    public static string ActionName(ComplaintAction action) => action switch
    {
        ComplaintAction.Claim => "claim",
        ComplaintAction.Assign => "assign",
        ComplaintAction.Resolve => "resolve",
        ComplaintAction.Reject => "reject",
        ComplaintAction.Reopen => "reopen",
        _ => "note"
    };

    /// <param name="assigneeAgentId">Yalnızca <see cref="ComplaintAction.Assign"/> için.</param>
    /// <param name="text">Resolve/Reject: müşteriye görünen açıklama; Reopen/Note: not.</param>
    public static ComplaintTransitionCheck Check(
        ComplaintAction action, ComplaintInfo complaint, ComplaintActor actor, string? assigneeAgentId, string? text)
    {
        var target = TargetOf(action, complaint.Status);
        if (target is null) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.InvalidTransition);
        if (!Permitted(action, complaint, actor)) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.Forbidden);

        switch (action)
        {
            case ComplaintAction.Assign when string.IsNullOrWhiteSpace(assigneeAgentId):
                return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.AssigneeRequired);
            case ComplaintAction.Resolve or ComplaintAction.Reject or ComplaintAction.Reopen or ComplaintAction.Note:
                if (string.IsNullOrWhiteSpace(text)) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.TextRequired);
                if (text.Trim().Length > MaxTextLength) return ComplaintTransitionCheck.Fail(ComplaintTransitionErrors.TooLong);
                break;
        }
        return ComplaintTransitionCheck.Ok(target);
    }

    /// <summary>Durum ve yetkiye göre sunulabilecek eylemler (girdi denetimi hariç) — panel düğmeleri.</summary>
    public static IReadOnlyList<ComplaintAction> AvailableActions(ComplaintInfo complaint, ComplaintActor actor) =>
        AllActions.Where(a => TargetOf(a, complaint.Status) is not null && Permitted(a, complaint, actor)).ToList();

    private static string? TargetOf(ComplaintAction action, string status) => action switch
    {
        ComplaintAction.Claim when status == WellKnown.ComplaintStatuses.Pending => WellKnown.ComplaintStatuses.InProgress,
        ComplaintAction.Assign when WellKnown.ComplaintStatuses.IsOpen(status) => WellKnown.ComplaintStatuses.InProgress,
        ComplaintAction.Resolve when status == WellKnown.ComplaintStatuses.InProgress => WellKnown.ComplaintStatuses.Resolved,
        ComplaintAction.Reject when status == WellKnown.ComplaintStatuses.InProgress => WellKnown.ComplaintStatuses.Rejected,
        ComplaintAction.Reopen when WellKnown.ComplaintStatuses.IsClosed(status) => WellKnown.ComplaintStatuses.InProgress,
        ComplaintAction.Note => status,
        _ => null
    };

    private static bool Permitted(ComplaintAction action, ComplaintInfo complaint, ComplaintActor actor)
    {
        var isAssignee = !actor.IsAdmin && actor.AgentId is not null
                         && string.Equals(actor.AgentId, complaint.AssignedAgentId, StringComparison.Ordinal);
        return action switch
        {
            ComplaintAction.Claim => !actor.IsAdmin && actor.AgentId is not null,
            ComplaintAction.Assign => actor.IsAdmin,
            _ => actor.IsAdmin || isAssignee
        };
    }
}
