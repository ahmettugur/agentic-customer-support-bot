// Domain/Services/ReasoningResultParser.cs
// LLM'den dönen JSON'u ReasoningResult'a parse eder.
// Sanitize, JSON çıkarma ve nested field parse helper'larını içerir.

using System.Text.Json;
using System.Text.RegularExpressions;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

/// <summary>
/// Reasoning LLM çıktısını yapılandırılmış <see cref="ReasoningResult"/>'a dönüştürür.
/// JSON parse, alan extract ve sanitize işlemlerini kapsar.
/// </summary>
public static class ReasoningResultParser
{
    /// <summary>
    /// LLM çıktısından ReasoningResult parse eder. JSON parse edilemezse
    /// düz metin olarak Analysis alanına wrap eder.
    /// </summary>
    public static ReasoningResult Parse(string text)
    {
        var json = ExtractJson(text);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JsonException("Reasoning output must be a JSON object.");

            double confidenceScore;
            if (root.TryGetProperty("confidenceScore", out var scoreEl) &&
                scoreEl.ValueKind == JsonValueKind.Number)
            {
                confidenceScore = Math.Clamp(scoreEl.GetDouble(), 0.0, 1.0);
            }
            else
            {
                confidenceScore = ReasoningResult.StringToScore(GetStringProperty(root, "confidence"));
            }

            var confidenceString = GetStringProperty(root, "confidence");
            if (string.IsNullOrWhiteSpace(confidenceString))
            {
                confidenceString = ReasoningResult.ScoreToString(confidenceScore);
            }

            // Sentiment parse — LLM "sentiment" ve "sentimentScore" alanları döndürür
            var sentimentLabel = GetStringProperty(root, "sentiment");
            var sentimentScore = TryGetDouble(root, "sentimentScore") ?? 0.5;
            if (string.IsNullOrWhiteSpace(sentimentLabel))
            {
                sentimentLabel = SentimentScoreToLabel(sentimentScore);
            }

            return new ReasoningResult
            {
                Analysis = SanitizeAnalysis(GetStringProperty(root, "analysis")),
                Steps = ParseSteps(root),
                Intent = GetStringProperty(root, "intent"),
                RequiredInfo = GetStringArrayProperty(root, "requiredInfo"),
                Confidence = confidenceString,
                Rationale = GetStringProperty(root, "rationale"),
                Assumptions = GetStringArrayProperty(root, "assumptions"),
                NextAction = GetStringProperty(root, "nextAction"),
                DecisionReason = GetStringProperty(root, "decisionReason"),
                ConfidenceScore = confidenceScore,
                SubTasks = ParseSubTasks(root),
                Sentiment = sentimentLabel,
                SentimentScore = sentimentScore
            };
        }
        catch (JsonException)
        {
            return new ReasoningResult
            {
                Analysis = SanitizeAnalysis(text.Length > 500 ? text[..500] : text),
                Steps = new List<ReasoningStep>(),
                Intent = WellKnown.Intents.Unknown,
                RequiredInfo = new List<string>(),
                Confidence = WellKnown.Confidence.Low,
                ConfidenceScore = 0.3,
                IsFallback = true
            };
        }
    }

    /// <summary>
    /// Analysis alanına sızmış nested JSON / fence marker'ları temizler.
    /// </summary>
    public static string SanitizeAnalysis(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();

        // Başındaki/sonundaki fence marker'ları at
        s = Regex.Replace(s, @"^(?:`{1,3}\s*json\s*|`{1,3})\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"`{1,3}\s*$", "");

        bool looksLikeJson =
            s.Contains("\"steps\":", StringComparison.Ordinal)
            || s.Contains("\"analysis\":", StringComparison.Ordinal)
            || s.Contains("\"intent\":", StringComparison.Ordinal);

        if (looksLikeJson)
        {
            var m = Regex.Match(
                s, @"""analysis""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return Regex.Unescape(m.Groups[1].Value);
            }
            return WellKnown.FallbackMessages.AnalysisParseFailed;
        }

        return s.Length > 500 ? s[..500] + "…" : s;
    }

    /// <summary>
    /// LLM çıktısından JSON bloğunu çıkarır. ```json ... ``` veya düz JSON destekler.
    /// </summary>
    public static string ExtractJson(string text)
    {
        var start = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text[start..end].Trim();
        }

        start = text.IndexOf("```", StringComparison.Ordinal);
        if (start >= 0)
        {
            start = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start) return text[start..end].Trim();
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return text[firstBrace..(lastBrace + 1)];
        }

        return text;
    }

    // ─── JSON helper'lar ───

    private static string GetStringProperty(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? ""
            : "";
    }

    private static List<string> GetStringArrayProperty(JsonElement root, string name)
    {
        var result = new List<string>();
        if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in prop.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// "steps" alanını parse eder. Hem object array hem legacy string array formatını destekler.
    /// </summary>
    private static List<ReasoningStep> ParseSteps(JsonElement root)
    {
        var result = new List<ReasoningStep>();
        if (!root.TryGetProperty("steps", out var stepsProp) ||
            stepsProp.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var order = 1;
        foreach (var item in stepsProp.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var desc = item.GetString();
                if (string.IsNullOrWhiteSpace(desc)) continue;
                result.Add(new ReasoningStep { Order = order++, Description = desc });
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var step = new ReasoningStep
                {
                    Order = TryGetInt(item, "order") ?? order,
                    Description = GetStringProperty(item, "description"),
                    Action = NullableString(item, "action"),
                    Premise = NullableString(item, "premise"),
                    Grounding = NullableString(item, "grounding"),
                    Confidence = TryGetDouble(item, "confidence"),
                    AlternativeRejected = NullableString(item, "alternativeRejected")
                };

                if (string.IsNullOrWhiteSpace(step.Description))
                {
                    step.Description = GetStringProperty(item, "step")
                                       ?? GetStringProperty(item, "label")
                                       ?? "";
                }

                if (!string.IsNullOrWhiteSpace(step.Description))
                {
                    result.Add(step);
                    order++;
                }
            }
        }

        return result;
    }

    private static int? TryGetInt(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number &&
            p.TryGetInt32(out var v)) return v;
        return null;
    }

    private static double? TryGetDouble(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number &&
            p.TryGetDouble(out var v)) return Math.Clamp(v, 0.0, 1.0);
        return null;
    }

    private static string? NullableString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
        {
            var s = p.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        return null;
    }

    /// <summary>
    /// "subTasks" alanını parse eder. Compound query decomposition için.
    /// </summary>
    private static List<SubTask> ParseSubTasks(JsonElement root)
    {
        var result = new List<SubTask>();
        if (!root.TryGetProperty("subTasks", out var arr) ||
            arr.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var order = 1;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            var sub = new SubTask
            {
                Order = TryGetInt(item, "order") ?? order,
                Intent = GetStringProperty(item, "intent"),
                Description = GetStringProperty(item, "description"),
                TargetAgent = GetStringProperty(item, "targetAgent"),
                Entities = ParseStringDict(item, "entities"),
                Dependencies = ParseIntList(item, "dependencies")
            };

            if (string.IsNullOrWhiteSpace(sub.Description) &&
                string.IsNullOrWhiteSpace(sub.TargetAgent) &&
                string.IsNullOrWhiteSpace(sub.Intent))
            {
                continue;
            }

            result.Add(sub);
            order++;
        }

        return result;
    }

    private static Dictionary<string, string> ParseStringDict(JsonElement el, string name)
    {
        var result = new Dictionary<string, string>();
        if (!el.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var v = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(v)) result[prop.Name] = v;
            }
            else if (prop.Value.ValueKind == JsonValueKind.Number)
            {
                result[prop.Name] = prop.Value.ToString();
            }
        }
        return result;
    }

    private static List<int> ParseIntList(JsonElement el, string name)
    {
        var result = new List<int>();
        if (!el.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var v))
                result.Add(v);
        }
        return result;
    }

    // ─── Sentiment helpers ───

    /// <summary>Sentiment skorundan etiket üretir.</summary>
    public static string SentimentScoreToLabel(double score) => score switch
    {
        <= WellKnown.SentimentThresholds.AngryThreshold => WellKnown.Sentiments.Angry,
        <= WellKnown.SentimentThresholds.NegativeThreshold => WellKnown.Sentiments.Negative,
        >= WellKnown.SentimentThresholds.PositiveThreshold => WellKnown.Sentiments.Positive,
        _ => WellKnown.Sentiments.Neutral
    };
}
