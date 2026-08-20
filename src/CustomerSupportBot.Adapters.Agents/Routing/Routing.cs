// Adapters.Agents/Routing/Routing.cs
// CustomerSupportChatManager için Strategy pattern altyapısı.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Agents.Routing;

internal readonly record struct RoutingResult(AIAgent Agent, string Branch);

internal static class Branches
{
    public const string FirstTurn = "first_turn";
    public const string PlanParseFailed = "plan_parse_failed";
    public const string PlanClarification = "plan_clarification";
    public const string PlanUnknownAgent = "plan_unknown_agent";
    public const string Plan = "plan";
    public const string PlanConstrained = "plan_constrained";
    public const string ReflectionMissing = "reflection_missing";
    public const string ReflectionEscalation = "reflection_escalation";
    public const string ReflectionHandoff = "reflection_handoff";
    public const string ReflectionComplete = "reflection_complete";
    public const string ReflectionConstrained = "reflection_constrained";
}

internal sealed class RoutingContext
{
    public required IReadOnlyDictionary<string, AIAgent> AgentsByName { get; init; }
    public required AIAgent PlanningAgent { get; init; }
    public required AIAgent ResponseAgent { get; init; }
    public required WorkflowGuardOptions Guards { get; init; }
    public required ILogger Logger { get; init; }
    public AIAgent? ConstrainedSpecialist { get; init; }

    public AIAgent? Resolve(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
            : AgentsByName.TryGetValue(name, out var a) ? a : null;

    public static bool IsSpecialistMessage(ChatMessage msg)
    {
        if (msg.AuthorName == null) return false;
        var prefixes = WellKnown.AgentNames.Specialists;
        for (int i = 0; i < prefixes.Length; i++)
        {
            if (msg.AuthorName.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string? GetSpecialistName(ChatMessage msg)
    {
        if (msg.AuthorName == null) return null;
        var prefixes = WellKnown.AgentNames.Specialists;
        for (int i = 0; i < prefixes.Length; i++)
        {
            if (msg.AuthorName.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                return prefixes[i];
        }
        return null;
    }
}

internal interface IRoutingStrategy
{
    ValueTask<RoutingResult?> TrySelectAsync(
        IReadOnlyList<ChatMessage> history,
        ChatMessage? lastMessage,
        CancellationToken ct);
}

internal sealed class FirstTurnStrategy : IRoutingStrategy
{
    private readonly RoutingContext _ctx;
    public FirstTurnStrategy(RoutingContext ctx) => _ctx = ctx;

    public ValueTask<RoutingResult?> TrySelectAsync(
        IReadOnlyList<ChatMessage> history, ChatMessage? lastMessage, CancellationToken ct)
    {
        for (int i = 0; i < history.Count; i++)
        {
            if (string.Equals(history[i].AuthorName,
                    WellKnown.AgentNames.Planning, StringComparison.Ordinal))
                return ValueTask.FromResult<RoutingResult?>(null);
        }
        return ValueTask.FromResult<RoutingResult?>(
            new RoutingResult(_ctx.PlanningAgent, Branches.FirstTurn));
    }
}

internal sealed class PlanRoutingStrategy : IRoutingStrategy
{
    private readonly RoutingContext _ctx;
    public PlanRoutingStrategy(RoutingContext ctx) => _ctx = ctx;

    public ValueTask<RoutingResult?> TrySelectAsync(
        IReadOnlyList<ChatMessage> history, ChatMessage? lastMessage, CancellationToken ct)
    {
        if (!string.Equals(lastMessage?.AuthorName,
                WellKnown.AgentNames.Planning, StringComparison.Ordinal))
            return ValueTask.FromResult<RoutingResult?>(null);

        var plan = PlanningResultParser.TryParse(lastMessage!.Text);
        if (plan == null)
        {
            _ctx.Logger.LogWarning(
                "Planning agent output could not be parsed; falling back to ResponseAgent");
            return Result(_ctx.ResponseAgent, Branches.PlanParseFailed);
        }

        if (plan.NeedsClarification)
        {
            return Result(_ctx.ResponseAgent, Branches.PlanClarification);
        }

        var planned = _ctx.Resolve(plan.SelectedAgent);
        if (planned == null)
        {
            _ctx.Logger.LogWarning(
                "Plan suggested unknown agent '{Agent}'; falling back to ResponseAgent",
                plan.SelectedAgent);
            return Result(_ctx.ResponseAgent, Branches.PlanUnknownAgent);
        }

        if (_ctx.ConstrainedSpecialist != null && planned != _ctx.ConstrainedSpecialist)
        {
            _ctx.Logger.LogWarning(
                "Plan selected {PlannedAgent}, but decomposed workflow is constrained to {AllowedAgent}",
                planned.Name, _ctx.ConstrainedSpecialist.Name);
            return Result(_ctx.ConstrainedSpecialist, Branches.PlanConstrained);
        }

        return Result(planned, Branches.Plan);
    }

    private static ValueTask<RoutingResult?> Result(AIAgent a, string b) =>
        ValueTask.FromResult<RoutingResult?>(new RoutingResult(a, b));
}

internal sealed class ReflectionRoutingStrategy : IRoutingStrategy
{
    private readonly RoutingContext _ctx;
    public ReflectionRoutingStrategy(RoutingContext ctx) => _ctx = ctx;

    public ValueTask<RoutingResult?> TrySelectAsync(
        IReadOnlyList<ChatMessage> history, ChatMessage? lastMessage, CancellationToken ct)
    {
        if (lastMessage == null || !RoutingContext.IsSpecialistMessage(lastMessage))
            return ValueTask.FromResult<RoutingResult?>(null);

        var specialistName = RoutingContext.GetSpecialistName(lastMessage) ?? "specialist";
        var reasoning = SpecialistReasoningParser.TryParse(lastMessage.Text, specialistName);
        var reflection = reasoning?.PostToolReflection;

        if (reflection == null)
            return Result(_ctx.ResponseAgent, Branches.ReflectionMissing);

        if (reflection.StatusEnum == TaskCompletionStatus.NeedsEscalation)
            return Result(_ctx.ResponseAgent, Branches.ReflectionEscalation);

        if (!string.IsNullOrWhiteSpace(reflection.HandoffSuggestion)
            && !reflection.HandoffSuggestion.Equals(
                WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase))
        {
            var handoffTarget = _ctx.Resolve(reflection.HandoffSuggestion);
            if (_ctx.ConstrainedSpecialist != null && handoffTarget != _ctx.ConstrainedSpecialist)
            {
                _ctx.Logger.LogWarning(
                    "Reflection handoff to {HandoffAgent} blocked; decomposed workflow is constrained to {AllowedAgent}",
                    reflection.HandoffSuggestion, _ctx.ConstrainedSpecialist.Name);
                return Result(_ctx.ResponseAgent, Branches.ReflectionConstrained);
            }
            if (handoffTarget != null)
                return Result(handoffTarget, Branches.ReflectionHandoff);

            _ctx.Logger.LogWarning(
                "Reflection suggested unknown agent '{Agent}'",
                reflection.HandoffSuggestion);
        }

        return Result(_ctx.ResponseAgent, Branches.ReflectionComplete);
    }

    private static ValueTask<RoutingResult?> Result(AIAgent a, string b) =>
        ValueTask.FromResult<RoutingResult?>(new RoutingResult(a, b));
}
