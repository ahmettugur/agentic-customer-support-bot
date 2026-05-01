// Agents/Routing/Routing.cs
// CustomerSupportChatManager için Strategy pattern altyapısı.
//
// Sıralı zincir:
//   FirstTurnStrategy
//     → PlanRoutingStrategy
//       → ReflectionRoutingStrategy
//         → LlmFallbackRoutingStrategy
//
// Manager bu listede ilk null-olmayan sonucu alır; hiçbiri seçim yapmazsa PlanningAgent'a düşer.

using System.Text.RegularExpressions;
using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Agents.Routing;

/// <summary>Routing kararının çıktısı.</summary>
internal readonly record struct RoutingResult(AIAgent Agent, string Branch);

/// <summary>Branch sabitleri — log/observability tutarlılığı için.</summary>
internal static class Branches
{
    public const string FirstTurn = "first_turn";
    public const string PlanParseFailed = "plan_parse_failed";
    public const string PlanClarification = "plan_clarification";
    public const string PlanUnknownAgent = "plan_unknown_agent";
    public const string Plan = "plan";
    public const string ReflectionMissing = "reflection_missing";
    public const string ReflectionEscalation = "reflection_escalation";
    public const string ReflectionHandoff = "reflection_handoff";
    public const string ReflectionComplete = "reflection_complete";
    public const string LlmFallback = "llm_fallback";
}

/// <summary>
/// Strateji metotlarına paslanan immutable bağlam.
/// Manager state'inden okunur — strateji manager'ı bilmez.
/// </summary>
internal sealed class RoutingContext
{
    public required IReadOnlyDictionary<string, AIAgent> AgentsByName { get; init; }
    public required AIAgent PlanningAgent { get; init; }
    public required AIAgent ResponseAgent { get; init; }
    public required WorkflowGuardOptions Guards { get; init; }
    public required IChatClient ChatClient { get; init; }
    public required string SelectionSystemPrompt { get; init; }
    public required ILogger Logger { get; init; }

    /// <summary>Strict allow-list — bilinmeyen isimler null döner.</summary>
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

/// <summary>Bir routing kararı verme stratejisi.</summary>
internal interface IRoutingStrategy
{
    /// <summary>
    /// Strateji bu turn için karar verebiliyorsa <see cref="RoutingResult"/>; aksi halde <c>null</c>.
    /// </summary>
    ValueTask<RoutingResult?> TrySelectAsync(
        IReadOnlyList<ChatMessage> history,
        ChatMessage? lastMessage,
        CancellationToken ct);
}

// ════════════════════════════════════════════════════════════════
// 1) FirstTurn — PlanningAgent henüz konuşmadıysa onu seç
// ════════════════════════════════════════════════════════════════
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

// ════════════════════════════════════════════════════════════════
// 2) Plan — Son mesaj PlanningAgent'tan ise plan JSON'una göre
// ════════════════════════════════════════════════════════════════
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

        if (plan.NeedsClarification ||
            plan.IntentConfidence < _ctx.Guards.PlanConfidenceThreshold)
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

        return Result(planned, Branches.Plan);
    }

    private static ValueTask<RoutingResult?> Result(AIAgent a, string b) =>
        ValueTask.FromResult<RoutingResult?>(new RoutingResult(a, b));
}

// ════════════════════════════════════════════════════════════════
// 3) Reflection — Son mesaj specialist ise PostToolReflection'a göre
// ════════════════════════════════════════════════════════════════
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

// ════════════════════════════════════════════════════════════════
// 4) LlmFallback — LLM'e sor, allow-list ile validate et
// ════════════════════════════════════════════════════════════════
internal sealed partial class LlmFallbackRoutingStrategy : IRoutingStrategy
{
    private readonly RoutingContext _ctx;
    public LlmFallbackRoutingStrategy(RoutingContext ctx) => _ctx = ctx;

    public async ValueTask<RoutingResult?> TrySelectAsync(
        IReadOnlyList<ChatMessage> history, ChatMessage? lastMessage, CancellationToken ct)
    {
        var window = Math.Max(1, _ctx.Guards.SelectionContextWindow);
        var prompt = new List<ChatMessage>(capacity: 1 + window)
        {
            new(ChatRole.System, _ctx.SelectionSystemPrompt)
        };
        foreach (var msg in history.TakeLast(window))
            prompt.Add(new ChatMessage(msg.Role, msg.Text ?? ""));

        Microsoft.Extensions.AI.ChatResponse response;
        try
        {
            response = await _ctx.ChatClient
                .GetResponseAsync(prompt, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogWarning(ex, "LLM-based agent selection call failed");
            return new RoutingResult(_ctx.PlanningAgent, Branches.LlmFallback);
        }

        var name = ExtractFirstAgentToken(response.Text?.Trim());
        var resolved = _ctx.Resolve(name);
        if (resolved == null)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _ctx.Logger.LogWarning(
                    "LLM returned unknown agent name '{Name}' (raw='{Raw}')",
                    name, response.Text);
            return new RoutingResult(_ctx.PlanningAgent, Branches.LlmFallback);
        }

        return new RoutingResult(resolved, Branches.LlmFallback);
    }

    private static string? ExtractFirstAgentToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = AgentTokenRegex().Match(text);
        return match.Success ? match.Value : text.Trim();
    }

    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*Agent", RegexOptions.Compiled)]
    private static partial Regex AgentTokenRegex();
}
