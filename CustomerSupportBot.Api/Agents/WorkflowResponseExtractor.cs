// Agents/WorkflowResponseExtractor.cs
// MAF workflow çıktısından anlamlı veri çıkarma: sonuç metni,
// planning, specialist reasoning, self-critique ve temizlik işlemleri.

using System.Text;
using System.Text.RegularExpressions;
using CustomerSupportBot.Api.Models;
using CustomerSupportBot.Api.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CustomerSupportBot.Api.Agents;

/// <summary>
/// MAF WorkflowOutputEvent'ten yapılandırılmış veri çıkaran yardımcı sınıf.
/// Sonuç metni, planning, specialist reasoning ve self-critique ayrıştırılır.
/// Ayrıca dahili routing mesajları ve teknik JSON blokları temizlenir.
/// </summary>
public static class WorkflowResponseExtractor
{
    /// <summary>
    /// Workflow çıktısından asıl kullanıcı yanıtını çıkarır.
    /// Öncelik: TERMINATE içeren ResponseAgent mesajı → son assistant mesajı.
    /// </summary>
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

    /// <summary>
    /// Workflow çıktısından PlanningAgent mesajını bulup yapılandırılmış PlanningResult'u parse eder.
    /// </summary>
    public static PlanningResult? ExtractPlanningFromOutput(WorkflowOutputEvent output)
    {
        if (output.Data is not IEnumerable<ChatMessage> chatMessages) return null;

        var planningMsg = chatMessages
            .FirstOrDefault(m => m.AuthorName == WellKnown.AgentNames.Planning
                                 || (m.Text?.Contains($"\"{WellKnown.JsonProperties.SelectedAgent}\"", StringComparison.Ordinal) ?? false));

        return planningMsg != null
            ? PlanningResultParser.TryParse(planningMsg.Text)
            : null;
    }

    /// <summary>
    /// Workflow çıktısından specialist agent mesajlarını tarayıp SpecialistReasoning kayıtlarını çıkarır.
    /// </summary>
    public static List<SpecialistReasoning> ExtractSpecialistReasoningsFromOutput(WorkflowOutputEvent output)
    {
        var results = new List<SpecialistReasoning>();
        if (output.Data is not IEnumerable<ChatMessage> chatMessages) return results;

        var specialistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in WellKnown.AgentNames.Specialists)
            specialistNames.Add(name);

        foreach (var msg in chatMessages)
        {
            if (string.IsNullOrWhiteSpace(msg.Text)) continue;
            if (msg.AuthorName == null) continue;

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

    /// <summary>
    /// "TERMINATE", "TERMINATE: reason=xxx", "TERMINATE (xxx)" formatlarını
    /// ve sonrasında kalan her şeyi (selfCritique JSON bloğu vb.) temizler.
    /// </summary>
    public static string RemoveTerminationMarkers(string result)
    {
        if (string.IsNullOrEmpty(result)) return result;

        var cleaned = Regex.Replace(
            result,
            @"TERMINATE(\s*[:\s]+reason\s*=\s*[a-zA-Z_]+|\s*\([^)]+\))?[\s\S]*$",
            "",
            RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }

    /// <summary>
    /// Specialist reasoning JSON blokları yanlışlıkla kullanıcıya sızarsa temizler.
    /// preToolCheck/resultConfidence/postToolReflection içerenleri keser.
    /// </summary>
    public static string RemoveTechnicalJsonBlocks(string result)
    {
        if (string.IsNullOrEmpty(result)) return result;

        const string technicalKeys =
            $"\"{WellKnown.JsonProperties.PreToolCheck}\"|\"{WellKnown.JsonProperties.ResultConfidence}\"|\"{WellKnown.JsonProperties.PostToolReflection}\"|\"{WellKnown.JsonProperties.SelfCritique}\"";

        // ```json ... ``` blokları
        var cleaned = Regex.Replace(
            result,
            @"```(?:json)?\s*\{[^`]*(?:" + technicalKeys + @")[^`]*\}\s*```",
            "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // Fence olmadan düz JSON bloğu
        cleaned = Regex.Replace(
            cleaned,
            @"\{(?:[^{}]|(?:\{[^{}]*\}))*(?:" + technicalKeys + @")(?:[^{}]|(?:\{[^{}]*\}))*\}",
            "",
            RegexOptions.Singleline);

        // Birden fazla ardışık newline'ı teke indir
        cleaned = Regex.Replace(cleaned, @"\n{3,}", "\n\n");
        return cleaned.Trim();
    }

    /// <summary>
    /// PlanningAgent'ın dahili yönlendirme mesajlarını tespit eder.
    /// Örn: "OrderInquiryAgent: sipariş durumunu sorgulayın"
    /// </summary>
    public static bool ContainsAgentRoutingMessage(string text)
    {
        foreach (var name in WellKnown.AgentNames.All)
        {
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// "TERMINATE: reason=&lt;value&gt;" veya "TERMINATE (reason)" formatlarından reason çıkarır.
    /// </summary>
    public static string? ParseTerminationReasonFromResult(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var match = Regex.Match(
            text, @"TERMINATE[:\s]+reason\s*=\s*([a-zA-Z_]+)",
            RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.ToLowerInvariant();

        match = Regex.Match(
            text, @"TERMINATE\s*\(([^)]+)\)",
            RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value.Trim().ToLowerInvariant();

        return null;
    }

    /// <summary>
    /// Streaming delta event'inin anonymous Data objesinden "text" alanını okur.
    /// </summary>
    public static string ExtractDeltaText(object? data)
    {
        if (data == null) return string.Empty;
        var prop = data.GetType().GetProperty("text");
        return prop?.GetValue(data)?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// MAF group chat iç executor'larını (GroupChatHost, router vb.) tespit eder.
    /// Bu tür event'ler kullanıcıya agent chip olarak gösterilmemelidir.
    /// </summary>
    public static bool IsInternalWorkflowExecutor(string executorId)
    {
        if (string.IsNullOrEmpty(executorId)) return true;
        return WellKnown.SystemExecutorPrefixes.Values
            .Any(p => executorId.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Metni kelime sınırına göre parçalar ve her parça arasında küçük bir gecikme ekler.
    /// Gerçek LLM streaming taklidi sağlar — workflow output'u bir kerede geldiği için.
    /// </summary>
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
        {
            yield return text.Substring(start);
        }
    }
}
