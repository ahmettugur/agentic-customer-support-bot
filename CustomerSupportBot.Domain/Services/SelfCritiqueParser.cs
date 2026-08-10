// Domain/Services/SelfCritiqueParser.cs
// ResponseAgent çıktısının SONUNDAKİ selfCritique JSON bloğunu çıkarır.
//
// Neden ayrı bir parser: SpecialistReasoningParser/PlanningResultParser "ilk fence"i alır —
// specialist/planning çıktısında JSON en BAŞTA olduğu için bu doğrudur. ResponseAgent'ta ise
// sıra terstir (önce kullanıcı yanıtı, sonra TERMINATE, en son selfCritique), üstelik yanıt
// metninin kendisi de kod bloğu içerebilir. Bu yüzden burada fence'e değil, "selfCritique"
// anahtarını çevreleyen nesneye bakılır ve parantezler string literal'leri atlanarak eşlenir.

using System.Text.Json;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Services;

public static class SelfCritiqueParser
{
    /// <summary>
    /// Ham ResponseAgent çıktısından <see cref="SelfCritique"/> çıkarır.
    /// Blok yoksa veya bozuksa <c>null</c> döner — kalite sinyali kaybolur ama tur etkilenmez.
    /// </summary>
    public static SelfCritique? TryParse(string? responseOutput)
    {
        if (string.IsNullOrWhiteSpace(responseOutput)) return null;

        var keyIndex = responseOutput.IndexOf(
            $"\"{WellKnown.JsonProperties.SelfCritique}\"", StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0) return null;

        var open = responseOutput.LastIndexOf('{', keyIndex);
        if (open < 0) return null;

        var close = FindMatchingBrace(responseOutput, open);
        if (close < 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(responseOutput[open..(close + 1)]);
            if (!doc.RootElement.TryGetProperty(WellKnown.JsonProperties.SelfCritique, out var el)
                || el.ValueKind != JsonValueKind.Object)
                return null;

            return new SelfCritique
            {
                AddressesUserQuery = GetBool(el, "addressesUserQuery", defaultValue: true),
                Tone = GetString(el, "tone") is { Length: > 0 } t ? t : WellKnown.CritiqueTones.Appropriate,
                Completeness = GetDouble(el, "completeness", defaultValue: 1.0),
                HallucinationRisk = GetDouble(el, "hallucinationRisk", defaultValue: 0.0),
                Sources = GetStringArray(el, "sources"),
                IssuesFound = GetStringArray(el, "issuesFound"),
                RevisionNeeded = GetBool(el, "revisionNeeded", defaultValue: false),
                RevisionNotes = GetString(el, "revisionNotes") is { Length: > 0 } n ? n : null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <paramref name="openIndex"/>'teki '{' ile eşleşen '}' konumu. String literal'lerin
    /// içindeki parantezler ve kaçış dizileri atlanır — aksi halde `revisionNotes` gibi
    /// serbest metin alanlarındaki bir '{' karakteri eşlemeyi bozardı.
    /// </summary>
    private static int FindMatchingBrace(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return i;
                    break;
            }
        }
        return -1;
    }

    private static string GetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? ""
            : "";

    private static bool GetBool(JsonElement root, string key, bool defaultValue)
    {
        if (!root.TryGetProperty(key, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static double GetDouble(JsonElement root, string key, double defaultValue)
    {
        if (!root.TryGetProperty(key, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDouble(out var d) => Math.Clamp(d, 0.0, 1.0),
            JsonValueKind.String when double.TryParse(
                el.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) => Math.Clamp(s, 0.0, 1.0),
            _ => defaultValue
        };
    }

    private static List<string> GetStringArray(JsonElement root, string key)
    {
        var list = new List<string>();
        if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.Array) return list;

        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
        }
        return list;
    }
}
