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

    /// <summary>Metin isteyen eylemler ve alan etiketi.</summary>
    public static string? TextPrompt(string action) => action switch
    {
        "resolve" or "reject" => "Müşteriye gönderilecek açıklama",
        "reopen" => "Yeniden açma gerekçesi (iç not)",
        "note" => "İç not (müşteriye gitmez)",
        _ => null
    };
}
