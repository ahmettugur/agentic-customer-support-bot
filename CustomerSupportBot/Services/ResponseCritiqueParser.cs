// Services/ResponseCritiqueParser.cs
// ResponseAgent çıktısındaki selfCritique JSON bloğunu parse eder.
// Beklenen format:
// ```json
// { "selfCritique": { "addressesUserQuery": true, "tone": "appropriate", ... } }
// ```

using System.Text.Json;
using CustomerSupportBot.Models;

namespace CustomerSupportBot.Services;

public static class ResponseCritiqueParser
{
    /// <summary>
    /// ResponseAgent çıktısından selfCritique'i parse eder. Bulamaz/parse edemezse null.
    /// </summary>
    public static ResponseCritique? TryParse(string? responseOutput)
    {
        if (string.IsNullOrWhiteSpace(responseOutput)) return null;

        // SelfCritique içeren ilk JSON bloğunu bul
        var json = ExtractCritiqueJson(responseOutput);
        if (json == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Ya doğrudan selfCritique objesi, ya da {"selfCritique": {...}} sarmalı
            JsonElement critiqueEl;
            if (root.TryGetProperty("selfCritique", out var inner)
                && inner.ValueKind == JsonValueKind.Object)
            {
                critiqueEl = inner;
            }
            else if (root.TryGetProperty("addressesUserQuery", out _)
                     || root.TryGetProperty("tone", out _)
                     || root.TryGetProperty("completeness", out _))
            {
                critiqueEl = root;
            }
            else
            {
                return null;
            }

            return new ResponseCritique
            {
                AddressesUserQuery = GetBool(critiqueEl, "addressesUserQuery", defaultValue: true),
                Tone = NormalizeTone(GetString(critiqueEl, "tone")),
                Completeness = GetDouble(critiqueEl, "completeness", 1.0),
                HallucinationRisk = GetDouble(critiqueEl, "hallucinationRisk", 0.0),
                Sources = GetStringArray(critiqueEl, "sources"),
                IssuesFound = GetStringArray(critiqueEl, "issuesFound"),
                RevisionNeeded = GetBool(critiqueEl, "revisionNeeded", defaultValue: false),
                RevisionNotes = GetString(critiqueEl, "revisionNotes")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// ResponseAgent mesajında selfCritique içeren JSON bloğunu bulur.
    /// Önce ```json fenced bloğu dener, yoksa inline JSON araması yapar.
    /// </summary>
    private static string? ExtractCritiqueJson(string text)
    {
        // 1) ```json ... ``` blokları — selfCritique içereni ara
        var fencePattern = System.Text.RegularExpressions.Regex.Matches(
            text,
            @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match m in fencePattern)
        {
            var candidate = m.Groups[1].Value;
            if (candidate.Contains("\"selfCritique\"", StringComparison.Ordinal)
                || IsLikelyCritiqueJson(candidate))
            {
                return candidate;
            }
        }

        // 2) İnline JSON — "selfCritique" anahtarı içeren dengeli blok
        var idx = text.IndexOf("\"selfCritique\"", StringComparison.Ordinal);
        if (idx >= 0)
        {
            // Geriye doğru '{' bulup dengeli blok çıkar
            var start = text.LastIndexOf('{', idx);
            if (start >= 0)
            {
                var end = FindMatchingBrace(text, start);
                if (end > start) return text[start..(end + 1)];
            }
        }

        return null;
    }

    private static bool IsLikelyCritiqueJson(string candidate)
    {
        return candidate.Contains("\"addressesUserQuery\"", StringComparison.Ordinal)
               || candidate.Contains("\"completeness\"", StringComparison.Ordinal)
               || candidate.Contains("\"hallucinationRisk\"", StringComparison.Ordinal);
    }

    private static int FindMatchingBrace(string text, int openIdx)
    {
        int depth = 0;
        bool inString = false;
        for (int i = openIdx; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && (i == 0 || text[i - 1] != '\\')) inString = !inString;
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static string NormalizeTone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "appropriate";
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "appropriate" or "ok" or "good" or "empathetic" or "empatik" or "samimi" => "appropriate",
            "too_formal" or "formal" or "resmi" => "too_formal",
            "too_casual" or "casual" or "samimi_fazla" => "too_casual",
            "impolite" or "kaba" or "harsh" => "impolite",
            "robotic" or "cold" or "mekanik" => "robotic",
            _ => "appropriate"
        };
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

    private static bool GetBool(JsonElement root, string key, bool defaultValue = false)
    {
        if (root.TryGetProperty(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.True) return true;
            if (el.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }

    private static double GetDouble(JsonElement root, string key, double defaultValue = 0.0)
    {
        if (root.TryGetProperty(key, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number) return Math.Clamp(el.GetDouble(), 0.0, 1.0);
            if (el.ValueKind == JsonValueKind.String)
                return Math.Clamp(ReasoningResult.StringToScore(el.GetString()), 0.0, 1.0);
        }
        return defaultValue;
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
}
