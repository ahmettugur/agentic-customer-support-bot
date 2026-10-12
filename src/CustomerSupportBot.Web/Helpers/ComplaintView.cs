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

    /// <summary>Durum rozeti/ikon tonu — panelin ortak <c>tone-*</c> renkleri.</summary>
    public static string StatusTone(string status) => status switch
    {
        "Beklemede" => "tone-warn",
        "İncelemede" => "tone-primary",
        "Çözüldü" => "tone-success",
        "Reddedildi" => "tone-danger",
        _ => "tone-neutral"
    };

    /// <summary>SLA yalnızca dikkat gerektirdiğinde etiketlenir; "ok" sessizdir.</summary>
    public static string? SlaLabel(string sla) => sla switch
    {
        "warn" => "SLA yaklaşıyor",
        "breach" => "SLA aşıldı",
        _ => null
    };

    /// <summary>Liste başlığı: şikayet metninin ilk dolu satırı, gerekirse "…" ile kısaltılmış.</summary>
    public static string Headline(string? text, int max = 80)
    {
        var line = (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (string.IsNullOrEmpty(line)) return "Şikayet";
        return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
    }

    /// <summary>Zaman çizelgesi noktasının tonu.</summary>
    public static string KindTone(string kind) => kind switch
    {
        "claimed" or "assigned" => "tone-primary",
        "reopened" => "tone-warn",
        "resolved" => "tone-success",
        "rejected" => "tone-danger",
        _ => "tone-neutral"
    };

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

    /// <summary>Düğme etkin mi: kapanış açıklama ister; not ve yeniden açma iç not ister; atama temsilci ister.</summary>
    public static bool CanSubmit(string action, string? resolution, string? note, string? assignee) => action switch
    {
        "resolve" or "reject" => !string.IsNullOrWhiteSpace(resolution),
        "reopen" or "note" => !string.IsNullOrWhiteSpace(note),
        "assign" => !string.IsNullOrWhiteSpace(assignee),
        _ => true
    };
}
