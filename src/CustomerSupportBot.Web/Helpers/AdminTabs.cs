namespace CustomerSupportBot.Web.Helpers;

/// <summary>
/// Admin panelinin adresli bölümleri (/admin?tab=…). Eskiden sekme yalnızca bellekteydi: sayfa yenilenince
/// ilk sekmeye dönülüyor, bir bölümün linki paylaşılamıyor, geri tuşu sekmeler arasında çalışmıyordu.
/// Kenar çubuğu ve Admin sayfası aynı listeyi kullanır; temsilci yalnızca kendi kuyruklarını açabilir.
/// </summary>
public static class AdminTabs
{
    public static readonly IReadOnlyList<string> All =
        ["approvals", "escalations", "chats", "history", "analytics", "orders", "conversations", "replies", "improvements"];

    private static readonly HashSet<string> AgentAllowed = ["escalations", "chats"];

    public static string Default(bool isAgent) => isAgent ? "escalations" : "approvals";

    /// <summary>Sorgu dizesinden (ör. "?tab=chats&amp;x=1") tab değerini okur; yoksa null.</summary>
    public static string? FromQuery(string? query)
    {
        if (string.IsNullOrEmpty(query)) return null;
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq  = part.IndexOf('=');
            var key = eq < 0 ? part : part[..eq];
            if (!string.Equals(key, "tab", StringComparison.OrdinalIgnoreCase)) continue;
            var value = eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..]);
            return value.Length == 0 ? null : value.ToLowerInvariant();
        }
        return null;
    }

    /// <summary>Geçersiz ya da role kapalı bir sekmeyi rolün varsayılanına çevirir.</summary>
    public static string Normalize(string? tab, bool isAgent)
    {
        var t = tab?.Trim().ToLowerInvariant();
        if (t is null || !All.Contains(t)) return Default(isAgent);
        if (isAgent && !AgentAllowed.Contains(t)) return Default(isAgent);
        return t;
    }
}
