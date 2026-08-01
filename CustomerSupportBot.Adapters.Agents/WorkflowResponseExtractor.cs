// Adapters.Agents/WorkflowResponseExtractor.cs
// MAF workflow output event'lerinden anlamlı veri çıkaran yardımcı sınıf.

using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Adapters.Agents;

public static class WorkflowResponseExtractor
{
    // LLM çıktısı (potansiyel olarak adversarial/prompt-injection kaynaklı metin) üzerinde
    // çalışan tüm regex'ler için ortak timeout — ReDoS'a karşı savunma katmanı.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    public static string ExtractResultFromOutput(WorkflowOutputEvent output)
    {
        if (output.Data is IEnumerable<ChatMessage> chatMessages)
        {
            var terminateMsg = chatMessages
                .LastOrDefault(m => m.Role == ChatRole.Assistant
                                    && m.Text != null
                                    && m.Text.Contains(WellKnown.Termination.Marker, StringComparison.Ordinal));
            if (terminateMsg != null) return terminateMsg.Text!;

            var lastAssistantMsg = chatMessages
                .LastOrDefault(m => m.Role == ChatRole.Assistant
                                    && !string.IsNullOrWhiteSpace(m.Text)
                                    && !ContainsAgentRoutingMessage(m.Text));

            return lastAssistantMsg?.Text
                   ?? chatMessages.LastOrDefault(m => m.Role == ChatRole.Assistant
                                                      && !string.IsNullOrWhiteSpace(m.Text))?.Text
                   ?? "";
        }
        if (output.Data is ChatMessage singleMsg) return singleMsg.Text ?? "";
        if (output.Data is string textData) return textData;
        return "";
    }

    public static PlanningResult? ExtractPlanningFromOutput(WorkflowOutputEvent output)
        => output.Data is IEnumerable<ChatMessage> chatMessages ? ExtractPlanning(chatMessages) : null;

    /// <summary>
    /// PlanningAgent mesajını herhangi bir <see cref="ChatMessage"/> koleksiyonundan çıkarır.
    /// Hem final <see cref="WorkflowOutputEvent"/> hem de ara <c>ExecutorCompletedEvent.Data</c>
    /// (aynı şekle sahip pending-state listesi) üzerinde çalışır — böylece plan/eskalasyon
    /// bilgisi workflow tamamlanmadan da (timeout/hata durumunda) elde edilebilir.
    /// </summary>
    public static PlanningResult? ExtractPlanning(IEnumerable<ChatMessage> chatMessages)
    {
        var planningMsg = chatMessages
            .FirstOrDefault(m => m.AuthorName == WellKnown.AgentNames.Planning
                                 || (m.Text?.Contains($"\"{WellKnown.JsonProperties.SelectedAgent}\"",
                                         StringComparison.Ordinal) ?? false));
        return planningMsg != null ? PlanningResultParser.TryParse(planningMsg.Text) : null;
    }

    public static List<SpecialistReasoning> ExtractSpecialistReasoningsFromOutput(WorkflowOutputEvent output)
        => output.Data is IEnumerable<ChatMessage> chatMessages
            ? ExtractSpecialistReasonings(chatMessages)
            : new List<SpecialistReasoning>();

    /// <summary>
    /// Specialist reasoning JSON'larını herhangi bir <see cref="ChatMessage"/> koleksiyonundan
    /// çıkarır — bkz. <see cref="ExtractPlanning"/> için aynı ara-durum gerekçesi.
    /// </summary>
    public static List<SpecialistReasoning> ExtractSpecialistReasonings(IEnumerable<ChatMessage> chatMessages)
    {
        var results = new List<SpecialistReasoning>();
        var specialistNames = new HashSet<string>(WellKnown.AgentNames.Specialists, StringComparer.OrdinalIgnoreCase);

        foreach (var msg in chatMessages)
        {
            if (string.IsNullOrWhiteSpace(msg.Text) || msg.AuthorName == null) continue;

            var matchedName = specialistNames.FirstOrDefault(n =>
                msg.AuthorName.StartsWith(n, StringComparison.OrdinalIgnoreCase));
            if (matchedName == null) continue;

            var hasSpecialistJson = msg.Text.Contains($"\"{WellKnown.JsonProperties.PreToolCheck}\"", StringComparison.Ordinal)
                                 || msg.Text.Contains($"\"{WellKnown.JsonProperties.ResultConfidence}\"", StringComparison.Ordinal)
                                 || msg.Text.Contains($"\"{WellKnown.JsonProperties.PostToolReflection}\"", StringComparison.Ordinal);
            if (!hasSpecialistJson) continue;

            var parsed = SpecialistReasoningParser.TryParse(msg.Text, matchedName);
            if (parsed != null) results.Add(parsed);
        }

        return results;
    }

    public static string RemoveTerminationMarkers(string result)
    {
        if (string.IsNullOrEmpty(result)) return result;
        try
        {
            var cleaned = Regex.Replace(
                result,
                @"TERMINATE(\s*[:\s]+reason\s*=\s*[a-zA-Z_]+|\s*\([^)]+\))?[\s\S]*$",
                "",
                RegexOptions.IgnoreCase,
                RegexTimeout);
            return cleaned.Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return result.Trim();
        }
    }

    public static string RemoveTechnicalJsonBlocks(string result)
    {
        if (string.IsNullOrEmpty(result)) return result;

        const string technicalKeys =
            $"\"{WellKnown.JsonProperties.PreToolCheck}\"|\"{WellKnown.JsonProperties.ResultConfidence}\"" +
            $"|\"{WellKnown.JsonProperties.PostToolReflection}\"|\"{WellKnown.JsonProperties.SelfCritique}\"";

        try
        {
            var cleaned = Regex.Replace(
                result,
                @"```(?:json)?\s*\{[^`]*(?:" + technicalKeys + @")[^`]*\}\s*```",
                "",
                RegexOptions.IgnoreCase | RegexOptions.Singleline,
                RegexTimeout);

            // Not: sadece tek seviye brace nesting'i eşleştirir — 2+ seviye derin, iç içe
            // JSON bloklarını kaçırabilir. Bu bilinen bir sınırlamadır (best-effort temizlik).
            cleaned = Regex.Replace(
                cleaned,
                @"\{(?:[^{}]|(?:\{[^{}]*\}))*(?:" + technicalKeys + @")(?:[^{}]|(?:\{[^{}]*\}))*\}",
                "",
                RegexOptions.Singleline,
                RegexTimeout);

            cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n", RegexOptions.None, RegexTimeout);
            return cleaned.Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return result.Trim();
        }
    }

    public static bool ContainsAgentRoutingMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var name in WellKnown.AgentNames.All)
        {
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string? ParseTerminationReasonFromResult(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        try
        {
            var match = Regex.Match(text, @"TERMINATE[:\s]+reason\s*=\s*([a-zA-Z_]+)", RegexOptions.IgnoreCase, RegexTimeout);
            if (match.Success) return match.Groups[1].Value.ToLowerInvariant();

            match = Regex.Match(text, @"TERMINATE\s*\(([^)]+)\)", RegexOptions.IgnoreCase, RegexTimeout);
            if (match.Success) return match.Groups[1].Value.Trim().ToLowerInvariant();

            return null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    public static string ExtractDeltaText(object? data) =>
        data is TextDeltaPayload payload ? payload.Text : string.Empty;

    public static bool IsInternalWorkflowExecutor(string executorId)
    {
        if (string.IsNullOrEmpty(executorId)) return true;
        return WellKnown.SystemExecutorPrefixes.Values
            .Any(p => executorId.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public static async IAsyncEnumerable<string> StreamTextInChunksAsync(
        string text,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ' ' || c == '\n')
            {
                yield return text.Substring(start, i - start + 1);
                start = i + 1;
                await Task.Delay(20, ct);
            }
        }
        if (start < text.Length)
            yield return text.Substring(start);
    }
}
