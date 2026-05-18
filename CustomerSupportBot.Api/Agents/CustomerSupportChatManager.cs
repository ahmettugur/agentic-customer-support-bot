// Agents/CustomerSupportChatManager.cs
// LLM tabanlı grup sohbet yöneticisi.
//
// Routing kararları Strategy pattern ile çözülür (Routing/Routing.cs):
//   FirstTurn → Plan → Reflection
//
// Bu sınıf yalnızca:
//   1) Strategy zincirini koşturmak,
//   2) Tek noktadan handoff sayımı (EnforceHandoffLimit),
//   3) Termination kontrollerini (TERMINATE marker / repeated tool calls)
// üstlenir.
//
// Per-request instance — mutable state izolasyonu için her workflow run yeni manager kullanır.

using System.Text.Json;
using CustomerSupportBot.Api.Agents.Routing;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Agents;

/// <summary>
/// Müşteri destek sohbeti için LLM tabanlı grup sohbet yöneticisi.
/// Not: Thread-safe değildir; her workflow çalıştırması için yeni instance kullanın.
/// </summary>
public class CustomerSupportChatManager : GroupChatManager
{
    private readonly ILogger<CustomerSupportChatManager> _logger;
    private readonly WorkflowGuardOptions _guards;
    private readonly AIAgent _planningAgent;
    private readonly AIAgent _responseAgent;
    private readonly IReadOnlyList<IRoutingStrategy> _strategies;

    // Per-run mutable state
    private readonly Dictionary<string, int> _handoffCounts =
        new(StringComparer.OrdinalIgnoreCase);

    public CustomerSupportChatManager(
        IReadOnlyList<AIAgent> agents,
        WorkflowGuardOptions guards,
        ILogger<CustomerSupportChatManager> logger)
    {
        _guards = guards;
        _logger = logger;

        var agentsByName = agents
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .ToDictionary(a => a.Name!, a => a, StringComparer.OrdinalIgnoreCase);

        if (!agentsByName.TryGetValue(WellKnown.AgentNames.Planning, out var planning))
            throw new InvalidOperationException(
                $"Required agent missing: {WellKnown.AgentNames.Planning}");
        _planningAgent = planning;

        if (!agentsByName.TryGetValue(WellKnown.AgentNames.Response, out var response))
            throw new InvalidOperationException(
                $"Required agent missing: {WellKnown.AgentNames.Response}");
        _responseAgent = response;

        var ctx = new RoutingContext
        {
            AgentsByName = agentsByName,
            PlanningAgent = _planningAgent,
            ResponseAgent = _responseAgent,
            Guards = _guards,
            Logger = _logger
        };

        _strategies =
        [
            new FirstTurnStrategy(ctx),
            new PlanRoutingStrategy(ctx),
            new ReflectionRoutingStrategy(ctx),
        ];
    }

    // ════════════════════════════════════════════════════════════════
    // Agent Selection — strateji zinciri
    // ════════════════════════════════════════════════════════════════

    protected override async ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var lastMessage = history.LastOrDefault();

        foreach (var strategy in _strategies)
        {
            var result = await strategy.TrySelectAsync(history, lastMessage, cancellationToken)
                .ConfigureAwait(false);
            if (result is { } r)
            {
                var agent = r.Agent;
                var branch = r.Branch;
                EnforceHandoffLimit(ref agent, ref branch);
                _logger.LogInformation(
                    "Agent selected via {Branch}: {Agent}", branch, agent.Name);
                return agent;
            }
        }

        // Hiçbir strateji karar vermediyse (teorik olarak ulaşılmaz — LlmFallback her zaman döner)
        _logger.LogWarning("No routing strategy matched; falling back to PlanningAgent");
        return _planningAgent;
    }

    /// <summary>
    /// Specialist seçildiğinde counter +1; limit aşıldıysa ResponseAgent'a zorla.
    /// Planning ve Response sayılmaz.
    /// </summary>
    private void EnforceHandoffLimit(ref AIAgent selected, ref string branch)
    {
        if (selected == _planningAgent || selected == _responseAgent) return;
        if (string.IsNullOrEmpty(selected.Name)) return;

        _handoffCounts.TryGetValue(selected.Name, out var count);
        if (count >= _guards.MaxHandoffsPerAgent)
        {
            _logger.LogInformation(
                "Handoff to {Target} blocked (count={Count}, max={Max}); forcing ResponseAgent",
                selected.Name, count, _guards.MaxHandoffsPerAgent);
            selected = _responseAgent;
            branch += "+handoff_limited";
            return;
        }
        _handoffCounts[selected.Name] = count + 1;
    }

    // ════════════════════════════════════════════════════════════════
    // Termination
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sonlandırma kontrolü: TERMINATE marker'ı veya tekrarlanan tool-call.
    /// Iteration limiti SDK'nın MaximumIterationCount property'si tarafından yönetilir.
    /// </summary>
    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        var lastText = history.LastOrDefault()?.Text ?? "";

        if (lastText.Contains(WellKnown.Termination.Marker, StringComparison.Ordinal))
            return ValueTask.FromResult(true);

        if (DetectRepeatedToolCall(history))
        {
            _logger.LogWarning("Terminating due to repeated tool call");
            return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(false);
    }

    /// <summary>History-derived (stateless) tekrarlanan tool-call tespiti.</summary>
    private bool DetectRepeatedToolCall(IReadOnlyList<ChatMessage> history)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var start = Math.Max(0, history.Count - 10);

        for (int i = start; i < history.Count; i++)
        {
            var contents = history[i].Contents;
            if (contents == null) continue;

            foreach (var content in contents)
            {
                if (content is FunctionCallContent fc)
                {
                    var sig = BuildToolSignature(fc.Name, fc.Arguments);
                    var c = counts.GetValueOrDefault(sig) + 1;
                    counts[sig] = c;
                    if (c >= _guards.MaxDuplicateToolCalls) return true;
                }
            }
        }
        return false;
    }

    private static readonly JsonSerializerOptions ToolSigJsonOpts = new() { WriteIndented = false };

    private static string BuildToolSignature(string toolName, IDictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0) return $"{toolName}:∅";

        var sorted = args.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        try
        {
            return $"{toolName}:{JsonSerializer.Serialize(sorted, ToolSigJsonOpts)}";
        }
        catch
        {
            var paramsStr = string.Join("|", sorted.Select(kv =>
                $"{kv.Key}={(kv.Value is null ? "<null>" : kv.Value.ToString())}"));
            return $"{toolName}:{paramsStr}";
        }
    }
}

