using System.Text.Json;

namespace CustomerSupportBot.Web.Helpers;

internal static class JsonExtensions
{
    public static string? TryGetProp(this JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) ? p.GetString() : null;

    public static bool? TryGetBool(this JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True  ? true
      : el.TryGetProperty(key, out var q) && q.ValueKind == JsonValueKind.False ? false
      : null;

    public static int? TryGetInt(this JsonElement el, string key) =>
        el.TryGetProperty(key, out var p) && p.TryGetInt32(out int v) ? v : null;

    public static List<string>? TryGetStringArray(this JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var p) || p.ValueKind != JsonValueKind.Array)
            return null;
        return p.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
    }
}
