using CustomerSupportBot.Models;
using CustomerSupportBot.Services;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Agents;

/// <summary>
/// Müşteri destek sohbeti için LLM tabanlı grup sohbet yöneticisi.
/// </summary>
public class CustomerSupportChatManager : GroupChatManager
{
    private readonly IReadOnlyList<AIAgent> _agents;
    private readonly IChatClient _chatClient;
    private readonly PromptService _prompts;
    private readonly int _maxMessages;
    private readonly int _maxDuplicateToolCalls;
    private int _initialHistoryCount = -1;

    // Tool-call imza sayaçları (aynı tool + aynı parametre combo'su)
    private readonly Dictionary<string, int> _toolCallCounts = new();

    // Dinamik handoff sayacı (ping-pong koruması)
    // Aynı specialist'e max 2 kez handoff yapılabilir; sonra ResponseAgent'a zorla.
    private readonly Dictionary<string, int> _handoffCounts = new();
    private const int MaxHandoffsPerAgent = 2;

    /// <summary>
    /// Son tespit edilen sonlandırma sebebi — ShouldTerminateAsync bunu set eder,
    /// Dışarıdan observe edilebilir.
    /// </summary>
    public string? LastTerminationReason { get; private set; }

    /// <summary>Son parse edilen specialist post-tool reflection.</summary>
    public Models.PostToolReflection? LastPostToolReflection { get; private set; }

    /// <summary>
    /// Yöneticiyi ajan listesi, IChatClient ve maksimum mesaj sayısı ile oluşturur.
    /// </summary>
    public CustomerSupportChatManager(
        IReadOnlyList<AIAgent> agents,
        IChatClient chatClient,
        PromptService prompts,
        int maxMessages = 20,
        int maxDuplicateToolCalls = 3)
    {
        _agents = agents;
        _chatClient = chatClient;
        _prompts = prompts;
        _maxMessages = maxMessages;
        _maxDuplicateToolCalls = maxDuplicateToolCalls;
    }

    /// <summary>
    /// Konuşma geçmişine göre bir sonraki ajanı LLM ile seçer.
    /// </summary>
    protected override async ValueTask<AIAgent> SelectNextAgentAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        // İlk çağrıda başlangıç mesaj sayısını kaydet
        if (_initialHistoryCount < 0)
        {
            _initialHistoryCount = history.Count;
        }

        // /1.4 — PlanningAgent'tan structured JSON geldi mi?
        // Geldiyse ve clarification gerekli/düşük confidence varsa direkt ResponseAgent'a yönlendir.
        var lastMessage = history.LastOrDefault();
        if (lastMessage?.AuthorName == WellKnown.AgentNames.Planning ||
            (lastMessage?.Text?.Contains($"\"{WellKnown.JsonProperties.SelectedAgent}\"", StringComparison.Ordinal) == true))
        {
            var plan = Services.PlanningResultParser.TryParse(lastMessage?.Text);
            if (plan != null)
            {
                LastPlanningResult = plan;

                // Clarification threshold: düşük confidence VEYA açık clarification isteği
                if (plan.NeedsClarification || plan.IntentConfidence < 0.7)
                {
                    var responseAgent = _agents.FirstOrDefault(a =>
                        a.Name?.Equals(WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase) == true);
                    if (responseAgent != null) return responseAgent;
                }

                // Normal flow: planın belirttiği ajanı seç
                if (!string.IsNullOrWhiteSpace(plan.SelectedAgent))
                {
                    var plannedAgent = _agents.FirstOrDefault(a =>
                        a.Name?.Equals(plan.SelectedAgent, StringComparison.OrdinalIgnoreCase) == true);
                    if (plannedAgent != null) return plannedAgent;
                }
            }
        }

        // Specialist'ten gelen postToolReflection ile dinamik handoff
        if (lastMessage != null && IsSpecialistMessage(lastMessage))
        {
            var specReasoning = Services.SpecialistReasoningParser.TryParse(
                lastMessage.Text, GetSpecialistName(lastMessage) ?? "specialist");
            var reflection = specReasoning?.PostToolReflection;

            if (reflection != null)
            {
                LastPostToolReflection = reflection;

                // Needs_escalation → ResponseAgent ki o da TERMINATE: reason=escalation_needed üretsin
                if (reflection.StatusEnum == TaskCompletionStatus.NeedsEscalation)
                {
                    var responseAgent = _agents.FirstOrDefault(a =>
                        a.Name?.Equals(WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase) == true);
                    if (responseAgent != null) return responseAgent;
                }

                // Dinamik handoff — başka bir specialist'e devret
                if (!string.IsNullOrWhiteSpace(reflection.HandoffSuggestion)
                    && !reflection.HandoffSuggestion.Equals(WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase))
                {
                    // Ping-pong guard: aynı ajana kaç kez handoff yapıldı?
                    var targetName = reflection.HandoffSuggestion;
                    _handoffCounts.TryGetValue(targetName, out var count);

                    if (count < MaxHandoffsPerAgent)
                    {
                        var target = _agents.FirstOrDefault(a =>
                            a.Name?.StartsWith(targetName, StringComparison.OrdinalIgnoreCase) == true);
                        if (target != null)
                        {
                            _handoffCounts[targetName] = count + 1;
                            return target;
                        }
                    }
                    // Limit aşıldı → ResponseAgent fallback
                }

                // Status=done/failed/partial/needs_followup → ResponseAgent
                var respAgent = _agents.FirstOrDefault(a =>
                    a.Name?.Equals(WellKnown.AgentNames.Response, StringComparison.OrdinalIgnoreCase) == true);
                if (respAgent != null) return respAgent;
            }
        }

        // Fallback: mevcut LLM-based selection
        var agentDescriptions = string.Join("\n",
            _agents.Select(a => $"- {a.Name}: {a.Description}"));

        var selectionPrompt = new List<ChatMessage>
        {
            new(ChatRole.System, _prompts.Render(
                "services/chat-manager-selection",
                new Dictionary<string, string?>
                {
                    ["AGENT_DESCRIPTIONS"] = agentDescriptions
                }))
        };

        // Son birkaç mesajı bağlam olarak ekle
        foreach (var msg in history.TakeLast(8))
        {
            selectionPrompt.Add(new ChatMessage(msg.Role, msg.Text ?? ""));
        }

        try
        {
            var response = await _chatClient.GetResponseAsync(
                selectionPrompt, cancellationToken: cancellationToken);
            var selectedName = response.Text?.Trim();

            var selectedAgent = _agents.FirstOrDefault(a =>
                a.Name != null && selectedName != null &&
                a.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));

            return selectedAgent ?? _agents[0];
        }
        catch
        {
            // LLM çağrısı başarısız olursa PlanningAgent'a yönlendir
            return _agents[0];
        }
    }

    /// <summary>
    /// Son parse edilen PlanningResult — trace store'a yansıtmak için dışarıdan okunabilir.
    /// </summary>
    public Models.PlanningResult? LastPlanningResult { get; private set; }

    /// <summary>
    /// Sohbetin sonlandırılıp sonlandırılmayacağını belirler.
    /// Termination = text_mention_termination | max_messages_termination
    /// Birleşiminin karşılığı.
    /// </summary>
    protected override ValueTask<bool> ShouldTerminateAsync(
        IReadOnlyList<ChatMessage> history,
        CancellationToken cancellationToken = default)
    {
        // Koşul 1: "TERMINATE" metin kontrolü
        // "TERMINATE: reason=<...>" formatı destekleniyor
        var lastMessage = history.LastOrDefault();
        var lastText = lastMessage?.Text ?? "";
        if (lastText.Contains(WellKnown.Termination.Marker, StringComparison.Ordinal))
        {
            LastTerminationReason = ParseTerminationReason(lastText) ?? WellKnown.Termination.ReasonCompleted;
            return ValueTask.FromResult(true);
        }

        // Koşul 2: Maksimum YENI mesaj sayısı kontrolü
        if (_initialHistoryCount < 0) _initialHistoryCount = history.Count;
        int newMessageCount = history.Count - _initialHistoryCount;
        if (newMessageCount >= _maxMessages)
        {
            LastTerminationReason = WellKnown.Termination.ReasonMaxMessages;
            return ValueTask.FromResult(true);
        }

        // Tekrar eden tool çağrısı kontrolü
        // Son mesajlardaki tool-call'ları tarayıp aynı imzanın 3+ kez
        // Tekrarlandığını tespit edersek workflow'u sonlandırıyoruz.
        if (DetectRepeatedToolCall(history))
        {
            LastTerminationReason = WellKnown.Termination.ReasonRepeatedToolCall;
            return ValueTask.FromResult(true);
        }

        return ValueTask.FromResult(false);
    }

    /// <summary>
    /// "TERMINATE: reason=<value>" veya "TERMINATE (reason)" formatlarından reason çıkarır.
    /// </summary>
    private static string? ParseTerminationReason(string text)
    {
        // Format 1: "TERMINATE: reason=completed" veya "TERMINATE reason=escalation"
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"TERMINATE[:\s]+reason\s*=\s*([a-zA-Z_]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.ToLowerInvariant();

        // Format 2: "TERMINATE (completed)" benzeri parantez içi
        match = System.Text.RegularExpressions.Regex.Match(
            text, @"TERMINATE\s*\(([^)]+)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.Trim().ToLowerInvariant();

        return null;
    }

    /// <summary>
    /// Son N mesajda aynı tool aynı parametrelerle kaç kez çağrıldı?
    /// MAF FunctionCallContent'lerini tarar. Eşik aşılırsa true döner.
    /// </summary>
    private bool DetectRepeatedToolCall(IReadOnlyList<ChatMessage> history)
    {
        // Sadece son 10 mesaj — geçmiş turları sayma
        var recent = history.TakeLast(10);

        foreach (var msg in recent)
        {
            if (msg.Contents == null) continue;

            foreach (var content in msg.Contents)
            {
                if (content is Microsoft.Extensions.AI.FunctionCallContent fc)
                {
                    var signature = BuildToolSignature(fc.Name, fc.Arguments);
                    _toolCallCounts.TryGetValue(signature, out var count);
                    count++;
                    _toolCallCounts[signature] = count;

                    if (count >= _maxDuplicateToolCalls) return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Tool adı + parametrelerin JSON hash'inden benzersiz imza üretir.
    /// </summary>
    private static string BuildToolSignature(string toolName, IDictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0) return $"{toolName}:∅";

        // Deterministik sıralı serileştirme
        var sorted = args.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var paramsJson = string.Join("|", sorted.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{toolName}:{paramsJson}";
    }

    // ─── yardımcıları ───

    private static readonly string[] SpecialistPrefixes = WellKnown.AgentNames.Specialists;

    /// <summary>Mesajı üreten ajan bir specialist mi?</summary>
    private static bool IsSpecialistMessage(ChatMessage msg)
    {
        if (msg.AuthorName == null) return false;
        return SpecialistPrefixes.Any(p =>
            msg.AuthorName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Specialist mesajından ajanın normalleştirilmiş adını çıkarır.</summary>
    private static string? GetSpecialistName(ChatMessage msg)
    {
        if (msg.AuthorName == null) return null;
        return SpecialistPrefixes.FirstOrDefault(p =>
            msg.AuthorName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}