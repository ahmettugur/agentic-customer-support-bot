namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Müşterinin geçmiş konuşmaları için saf yardımcılar (listeleme, açılışta geri yükleme kararı, zaman etiketi).
/// Zaman etiketi ELLE biçimlenir: Blazor WASM'de tr-TR ICU verisi yoktur, kültürlü biçimleme tarayıcıya göre değişirdi.
/// </summary>
public static class ChatHistory
{
    /// <summary>Listede gösterilecek en fazla konuşma.</summary>
    public const int MaxListed = 30;

    public const string UntitledTitle = "Yeni sohbet";

    /// <summary>Mesajı olmayan oturumlar (açılıp hiçbir şey yazılmayanlar) gizlenir; en yeni üstte.</summary>
    public static List<SessionInfo> Visible(IEnumerable<SessionInfo> sessions) =>
        sessions.Where(s => s.MessageCount > 0)
                .OrderByDescending(s => s.LastActivity)
                .Take(MaxListed)
                .ToList();

    public static string TitleOf(SessionInfo session) =>
        string.IsNullOrWhiteSpace(session.Title) ? UntitledTitle : session.Title;

    /// <summary>
    /// Açılışta hangi konuşma geri yüklenir? Yalnızca bu tarayıcıda SON AKTİF olarak hatırlanan oturum, hâlâ var ve mesajı
    /// varsa. "Yeni sohbet"e basıp yenileyen kullanıcıda hatırlanan oturum silinmiştir; eski konuşma geri gelmez.
    /// </summary>
    public static string? ResolveRestore(IEnumerable<SessionInfo> sessions, string? active)
    {
        if (string.IsNullOrWhiteSpace(active)) return null;
        return sessions.Any(s => s.SessionId == active && s.MessageCount > 0) ? active : null;
    }

    /// <summary>Sunucu rolü ("user" / "bot") → arayüz rolü ("user" / "assistant").</summary>
    public static string UiRole(string serverRole) => serverRole == "user" ? "user" : "assistant";

    /// <summary>"Bugün 14:20", "Dün 09:05", "3 gün önce", ya da "12.10.2026". <paramref name="offset"/> tarayıcının UTC farkı.</summary>
    public static string RelativeTime(DateTimeOffset at, DateTimeOffset now, TimeSpan offset)
    {
        var local = at.ToOffset(offset);
        var days = (now.ToOffset(offset).Date - local.Date).Days;
        var clock = $"{local.Hour:00}:{local.Minute:00}";
        return days switch
        {
            <= 0 => $"Bugün {clock}",
            1 => $"Dün {clock}",
            < 7 => $"{days} gün önce",
            _ => $"{local.Day:00}.{local.Month:00}.{local.Year}"
        };
    }
}
