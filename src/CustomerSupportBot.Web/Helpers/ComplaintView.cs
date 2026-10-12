using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Helpers;

/// <summary>Şikayet paneli için saf görüntü yardımcıları. Hangi düğmenin görüneceğine sunucu (<c>actions</c>) karar verir.</summary>
public static class ComplaintView
{
    public static readonly IReadOnlyList<(string Value, string Label)> StatusFilters =
    [
        ("open", "Açık"), ("Beklemede", "Beklemede"), ("İncelemede", "İncelemede"),
        ("Çözüldü", "Çözüldü"), ("Reddedildi", "Reddedildi"), ("all", "Tümü")
    ];

    public static string ToneClass(string sla) => sla switch
    {
        "breach" => "sla-breach",
        "warn" => "sla-warn",
        "ok" => "sla-ok",
        _ => ""
    };

    public static string ActionLabel(string action) => action switch
    {
        "claim" => "Üstlen",
        "assign" => "Ata",
        "resolve" => "Çözüldü",
        "reject" => "Reddet",
        "reopen" => "Yeniden aç",
        "note" => "Not ekle",
        _ => action
    };

    public static string KindLabel(string kind) => kind switch
    {
        "created" => "Oluşturuldu",
        "claimed" => "Üstlenildi",
        "assigned" => "Atandı",
        "resolved" => "Çözüldü",
        "rejected" => "Reddedildi",
        "reopened" => "Yeniden açıldı",
        "note" => "Not",
        _ => kind
    };

    public static string Age(DateTime createdAtUtc, DateTime nowUtc)
    {
        var span = nowUtc - createdAtUtc;
        if (span.TotalHours < 1) return $"{Math.Max(0, (int)span.TotalMinutes)} dk";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} sa";
        return $"{(int)span.TotalDays} gün";
    }

    /// <summary>Admin: üstlenilmeyi bekleyenler. Temsilci: bekleyenler + kendisine atanan açıklar.</summary>
    public static int BadgeCount(ComplaintCountsResponse c, bool isAgent) => isAgent ? c.Pending + c.AssignedToMe : c.Pending;

    // İki AYRI alan: müşteriye giden açıklama yalnız Çözüldü/Reddet için; iç not ise not, yeniden açma
    // gerekçesi ve kapanıştaki isteğe bağlı iç not için. Tek ortak kutu, müşteriye yazılan metnin yanlışlıkla
    // iç not olarak (ya da tersi) kaydedilmesine yol açıyordu.

    public static bool ShowsResolutionField(IReadOnlyCollection<string> actions) =>
        actions.Contains("resolve") || actions.Contains("reject");

    public static bool ShowsNoteField(IReadOnlyCollection<string> actions) =>
        actions.Any(a => a is "resolve" or "reject" or "reopen" or "note");

    public static string NoteLabel(IReadOnlyCollection<string> actions) =>
        actions.Contains("reopen") ? "Yeniden açma gerekçesi / iç not (müşteriye gitmez)" : "İç not (müşteriye gitmez)";

    /// <summary>Düğme etkin mi: kapanış açıklama ister; not ve yeniden açma iç not ister; atama temsilci ister.</summary>
    public static bool CanSubmit(string action, string? resolution, string? note, string? assignee) => action switch
    {
        "resolve" or "reject" => !string.IsNullOrWhiteSpace(resolution),
        "reopen" or "note" => !string.IsNullOrWhiteSpace(note),
        "assign" => !string.IsNullOrWhiteSpace(assignee),
        _ => true
    };
}
