// Domain/Services/SpecialistReasoningParser.cs
// Specialist agent çıktısından yapılandırılmış reasoning'i çıkarır.
// Beklenen format agent mesajı içinde bir JSON bloğu:
// ```json
// { "preToolCheck": {...}, "resultConfidence": 0.9, "resultNotes": "..." }
// ```

using System.Text.Json;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

public static class SpecialistReasoningParser
{
    /// <summary>
    /// Specialist agent çıktısından SpecialistReasoning'i parse eder.
    /// JSON bulunamaz/parse edilemezse null döner.
    /// </summary>
    public static SpecialistReasoning? TryParse(string? agentOutput, string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentOutput)) return null;

        var json = ExtractJsonBlock(agentOutput);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Sadece "preToolCheck", "resultConfidence" veya "postToolReflection" key'i
            // Varsa bunu specialist reasoning kabul et — diğer JSON'ları (PlanningAgent) atla
            var hasPreCheck = root.TryGetProperty("preToolCheck", out _);
            var hasConfidence = root.TryGetProperty("resultConfidence", out _);
            var hasReflection = root.TryGetProperty("postToolReflection", out _);
            if (!hasPreCheck && !hasConfidence && !hasReflection) return null;

            var result = new SpecialistReasoning
            {
                AgentName = agentName,
                ResultNotes = GetString(root, "resultNotes")
            };

            if (root.TryGetProperty("resultConfidence", out var rc))
            {
                if (rc.ValueKind == JsonValueKind.Number)
                    result.ResultConfidence = Math.Clamp(rc.GetDouble(), 0.0, 1.0);
                else if (rc.ValueKind == JsonValueKind.String)
                    result.ResultConfidence = ReasoningResult.StringToScore(rc.GetString());
            }

            if (hasPreCheck && root.GetProperty("preToolCheck").ValueKind == JsonValueKind.Object)
            {
                result.PreToolCheck = ParsePreToolCheck(root.GetProperty("preToolCheck"));
            }

            // PostToolReflection objesini parse et
            if (hasReflection && root.GetProperty("postToolReflection").ValueKind == JsonValueKind.Object)
            {
                result.PostToolReflection = ParsePostToolReflection(root.GetProperty("postToolReflection"));
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PreToolCheck ParsePreToolCheck(JsonElement el)
    {
        var check = new PreToolCheck
        {
            RequiredParams = GetStringArray(el, "requiredParams"),
            CollectedParams = GetStringArray(el, "collectedParams"),
            MissingParams = GetStringArray(el, "missingParams"),
            CanProceed = GetBool(el, "canProceed"),
            Reasoning = GetString(el, "reasoning")
        };

        if (el.TryGetProperty("confidence", out var conf))
        {
            if (conf.ValueKind == JsonValueKind.Number)
                check.Confidence = Math.Clamp(conf.GetDouble(), 0.0, 1.0);
            else if (conf.ValueKind == JsonValueKind.String)
                check.Confidence = ReasoningResult.StringToScore(conf.GetString());
        }

        return check;
    }

    /// <summary>Post-tool reflection JSON objesini parse eder.</summary>
    private static PostToolReflection ParsePostToolReflection(JsonElement el)
    {
        var reflection = new PostToolReflection
        {
            TaskComplete = GetBool(el, "taskComplete"),
            Status = NormalizeStatus(GetString(el, "status")),
            HandoffReason = GetString(el, "handoffReason"),
            MissingContext = GetStringArray(el, "missingContext"),
            Summary = GetString(el, "summary")
        };

        // HandoffSuggestion — null veya agent adı
        var handoff = GetString(el, "handoffSuggestion");
        reflection.HandoffSuggestion = string.IsNullOrWhiteSpace(handoff)
            || handoff.Equals("null", StringComparison.OrdinalIgnoreCase)
            || handoff.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? null
                : handoff.Trim();

        return reflection;
    }

    private static string NormalizeStatus(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return WellKnown.TaskStatuses.Done;
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            WellKnown.TaskStatuses.Done or WellKnown.TaskStatuses.Completed or WellKnown.TaskStatuses.Complete or WellKnown.TaskStatuses.Success => WellKnown.TaskStatuses.Done,
            WellKnown.TaskStatuses.NeedsFollowUp or WellKnown.TaskStatuses.Followup or WellKnown.TaskStatuses.NeedsFollowupNoUnderscore => WellKnown.TaskStatuses.NeedsFollowUp,
            WellKnown.TaskStatuses.NeedsEscalation or WellKnown.TaskStatuses.Escalation or WellKnown.TaskStatuses.Escalate => WellKnown.TaskStatuses.NeedsEscalation,
            WellKnown.TaskStatuses.Failed or WellKnown.TaskStatuses.Error or WellKnown.TaskStatuses.Fail => WellKnown.TaskStatuses.Failed,
            WellKnown.TaskStatuses.Partial or WellKnown.TaskStatuses.Incomplete => WellKnown.TaskStatuses.Partial,
            _ => WellKnown.TaskStatuses.Done
        };
    }

    /// <summary>
    /// "```json ... ```" fence'i içindeki veya düz JSON bloğunu çıkarır.
    /// </summary>
    private static string? ExtractJsonBlock(string text)
    {
        // ```json ... ```
        var start = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text[start..end].Trim();
        }

        // ``` ... ```
        start = text.IndexOf("```", StringComparison.Ordinal);
        if (start >= 0)
        {
            start = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text[start..end].Trim();
        }

        // Düz JSON — preToolCheck/resultConfidence key'lerini içeren tek bir obje
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text[firstBrace..(lastBrace + 1)];
        }
        return null;
    }

    private static string GetString(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
            if (el.ValueKind == JsonValueKind.Null) return "";
            return el.ToString();
        }
        return "";
    }

    private static List<string> GetStringArray(JsonElement root, string key)
    {
        var list = new List<string>();
        if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
        }
        return list;
    }

    private static bool GetBool(JsonElement root, string key)
    {
        if (root.TryGetProperty(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }
}
