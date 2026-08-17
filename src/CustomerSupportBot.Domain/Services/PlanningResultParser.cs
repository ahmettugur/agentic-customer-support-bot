// Domain/Services/PlanningResultParser.cs
// PlanningAgent çıktısındaki yapılandırılmış JSON'u parse eder.
// Çıktı: ```json { ... } ``` fence'leri içinde JSON + ardından routing metni.

using System.Text.Json;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

public static class PlanningResultParser
{
    /// <summary>
    /// PlanningAgent çıktısından yapılandırılmış PlanningResult'u çıkarır.
    /// JSON bulunamaz/parse edilemezse null döner.
    /// </summary>
    public static PlanningResult? TryParse(string? planningOutput)
    {
        if (string.IsNullOrWhiteSpace(planningOutput)) return null;

        var json = ExtractJsonBlock(planningOutput);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new PlanningResult
            {
                SupportingEvidence = GetStringArray(root, "supportingEvidence"),
                SelectedAgent = GetString(root, "selectedAgent"),
                Rationale = GetString(root, "rationale"),
                AlternativesRejected = GetAlternatives(root, "alternativesRejected"),
                NeedsClarification = GetBool(root, "needsClarification"),
                ClarificationQuestion = GetString(root, "clarificationQuestion"),
                TaskDescription = GetString(root, "taskDescription")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// "```json ... ```" fence'i veya düz JSON bloğu çıkarır.
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

        // Düz JSON — ilk '{' ile son '}' arası
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

    private static List<RejectedAlternative> GetAlternatives(JsonElement root, string key)
    {
        var list = new List<RejectedAlternative>();
        if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                list.Add(new RejectedAlternative
                {
                    Agent = GetString(item, "agent"),
                    Reason = GetString(item, "reason")
                });
            }
        }
        return list;
    }
}
