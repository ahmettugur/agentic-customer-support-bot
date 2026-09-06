// Adapters.Agents/CustomerSupportChatManager.cs
// LLM tabanlı grup sohbet yöneticisi.

using System.Text.Json;
using CustomerSupportBot.Adapters.Agents.Routing;
using CustomerSupportBot.Domain.Model;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

// Microsoft.Extensions.AI 10.9.0 kendi RoutingContext tipini ekledi ve buradaki
// `using Microsoft.Extensions.AI;` ile bizimki (Routing/Routing.cs) arasında CS0104
// belirsizliği doğurdu. Alias, hangisinin kastedildiğini adıyla sabitler — MEAI ileride
// başka bir ad daha çakıştırsa bile bu dosya etkilenmez.
using RoutingContext = CustomerSupportBot.Adapters.Agents.Routing.RoutingContext;

namespace CustomerSupportBot.Adapters.Agents;

/// <summary>
/// Müşteri destek sohbeti için LLM tabanlı grup sohbet yöneticisi.
/// </summary>
public class CustomerSupportChatManager : GroupChatManager
{
    private readonly ILogger<CustomerSupportChatManager> _logger;
    private readonly WorkflowGuardOptions _guards;
    private readonly AIAgent _planningAgent;
    private readonly AIAgent _responseAgent;
    private readonly IReadOnlyList<IRoutingStrategy> _strategies;

    // NOT — checkpoint restore ön koşulu: GroupChatManager.IterationCount framework tarafından
    // otomatik checkpoint'lenir, ama bu dictionary DEĞİL (bkz. GroupChatManager.OnCheckpointingAsync/
    // OnCheckpointRestoredAsync — RoundRobinGroupChatManager kendi cursor'ı için bu deseni kullanıyor).
    // Bugün CheckpointManager bağlı olmadığı için bu hook'lar hiç ateşlenmiyor, dolayısıyla bu bir bug
    // değil. Ama checkpointing ileride bağlanırsa, bu iki hook override edilip _handoffCounts burada
    // persist/restore edilmeden restart sonrası sessizce sıfırlanır (handoff-limit garantisi zayıflar).
    private readonly Dictionary<string, int> _handoffCounts =
        new(StringComparer.OrdinalIgnoreCase);

    public CustomerSupportChatManager(
        IReadOnlyList<AIAgent> agents,
        WorkflowGuardOptions guards,
        ILogger<CustomerSupportChatManager> logger,
        string? constrainedSpecialistName = null)
    {
        _guards = guards;
        _logger = logger;
        MaximumIterationCount = guards.MaxIterations;

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

        AIAgent? constrainedSpecialist = null;
        if (!string.IsNullOrWhiteSpace(constrainedSpecialistName))
        {
            if (!WellKnown.AgentNames.Specialists.Contains(
                    constrainedSpecialistName, StringComparer.OrdinalIgnoreCase)
                || !agentsByName.TryGetValue(constrainedSpecialistName, out constrainedSpecialist))
            {
                throw new InvalidOperationException(
                    $"Invalid constrained specialist: {constrainedSpecialistName}");
            }
        }

        var ctx = new RoutingContext
        {
            AgentsByName = agentsByName,
            PlanningAgent = _planningAgent,
            ResponseAgent = _responseAgent,
            Guards = _guards,
            Logger = _logger,
            ConstrainedSpecialist = constrainedSpecialist
        };

        _strategies =
        [
            new FirstTurnStrategy(ctx),
            new PlanRoutingStrategy(ctx),
            new ReflectionRoutingStrategy(ctx),
        ];
    }

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

        _logger.LogWarning("No routing strategy matched; falling back to PlanningAgent");
        return _planningAgent;
    }

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

    protected override async ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        if (await base.ShouldTerminateAsync(history, cancellationToken).ConfigureAwait(false))
            return true;

        var last = history.LastOrDefault();
        if (last?.Role == ChatRole.Assistant
            && string.Equals(last.AuthorName, WellKnown.AgentNames.Response, StringComparison.Ordinal)
            && TerminationProtocol.FindStart(last.Text ?? "") >= 0)
            return true;

        if (DetectRepeatedToolCall(history))
        {
            _logger.LogWarning("Terminating due to repeated tool call");
            return true;
        }

        return false;
    }

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
