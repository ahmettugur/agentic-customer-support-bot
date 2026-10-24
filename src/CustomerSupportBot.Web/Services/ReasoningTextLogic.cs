namespace CustomerSupportBot.Web.Services;

/// <summary>
/// "Düşünce süreci" panelinde reasoning_complete gelmeden önce gösterilecek metin (saf).
/// Sunucu artık yalnızca düz analiz metnini akıtır; eski sunucu ham JSON ({"analysis":"…",…}) akıtıyordu. İkisi de doğru gösterilir.
/// </summary>
public static class ReasoningTextLogic
{
    public static string PartialAnalysis(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        const string marker = "\"analysis\"";
        var idx = raw.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            // Ham JSON'un başı henüz gelmiş olabilir ("{\"int") — onu gösterme; düz metinse olduğu gibi göster.
            return raw.TrimStart().StartsWith('{') ? "" : raw;

        var colon = raw.IndexOf(':', idx + marker.Length);
        if (colon < 0) return "";
        var quoteStart = raw.IndexOf('"', colon + 1);
        if (quoteStart < 0) return "";

        var valueStart = quoteStart + 1;
        for (var i = valueStart; i < raw.Length; i++)
        {
            if (raw[i] == '\\') { i++; continue; }
            if (raw[i] == '"') return raw[valueStart..i];
        }
        return raw[valueStart..];
    }
}
