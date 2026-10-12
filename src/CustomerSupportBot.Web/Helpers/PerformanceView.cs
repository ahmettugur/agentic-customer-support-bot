using System.Globalization;

namespace CustomerSupportBot.Web.Helpers;

/// <summary>
/// Performans ekranının saf görüntü yardımcıları: biçimler, Türkçe etiketler, oran tonları.
/// Ondalıklar kültürden BAĞIMSIZ yazılır (invariant + virgül): Blazor WASM tarayıcıya yalnızca seçili dillerin ICU
/// verisini yüklediği için "tr-TR" orada invariant'a düşüyor ve "9.3 sn" görünüyordu.
/// </summary>
public static class PerformanceView
{
    private static string Dec(double value, string format) =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');

    public const int DefaultRangeDays = 7;

    public static readonly IReadOnlyList<(int Days, string Label)> Ranges =
        [(1, "Son 24 saat"), (7, "Son 7 gün"), (30, "Son 30 gün"), (90, "Son 90 gün")];

    public static string Percent(double? rate) =>
        rate is { } r ? $"%{Dec(Math.Round(r * 100, MidpointRounding.AwayFromZero), "0")}" : "—";

    /// <summary>Saniye → "45 sn", "1 dk 30 sn", "1 sa 30 dk", "2 gün 7 sa".</summary>
    public static string Duration(double? seconds)
    {
        if (seconds is not { } s || s < 0) return "—";
        var total = (long)Math.Round(s);
        if (total < 60) return $"{total} sn";
        if (total < 3600) return Join(total / 60, "dk", total % 60, "sn");
        if (total < 86400) return Join(total / 3600, "sa", total % 3600 / 60, "dk");
        return Join(total / 86400, "gün", total % 86400 / 3600, "sa");
    }

    /// <summary>Milisaniye → "850 ms", "2,5 sn", "1 dk 30 sn"; 0 → "—" (ölçüm yok).</summary>
    public static string DurationMs(double ms)
    {
        if (ms <= 0) return "—";
        if (ms < 1000) return $"{Dec(ms, "0")} ms";
        if (ms < 60_000) return $"{Dec(ms / 1000, "0.#")} sn";
        return Duration(ms / 1000);
    }

    /// <summary>Müşteri puanı: "4,5".</summary>
    public static string Rating(double stars) => Dec(stars, "0.0");

    private static string Join(long major, string majorUnit, long minor, string minorUnit) =>
        minor > 0 ? $"{major} {majorUnit} {minor} {minorUnit}" : $"{major} {majorUnit}";

    public static string AgentLabel(string agentName) => agentName switch
    {
        "PlanningAgent" => "Planlama",
        "ProductAgent" => "Ürün",
        "OrderAgent" => "Sipariş",
        "ComplaintAgent" => "Şikayet",
        "HumanHandoffAgent" => "Temsilciye aktarım",
        "ResponseAgent" => "Yanıt",
        _ => agentName
    };

    public static string ToolLabel(string toolName) => toolName switch
    {
        "order_status_tool" => "Sipariş durumu",
        "get_last_order_tool" => "Son sipariş",
        "get_all_orders_tool" => "Sipariş listesi",
        "order_placement_tool" => "Sipariş oluşturma",
        "order_cancel_tool" => "Sipariş iptali",
        "return_request_tool" => "İade talebi",
        "product_inquiry_tool" => "Ürün sorgusu",
        "product_list_tool" => "Ürün listesi",
        "complaint_registration_tool" => "Şikayet kaydı",
        "complaint_status_tool" => "Şikayet durumu",
        "get_all_complaints_tool" => "Şikayet listesi",
        "human_handoff_tool" => "Temsilciye aktarım",
        _ => toolName
    };

    public static string TerminationLabel(string reason) => reason switch
    {
        "completed" => "Tamamlandı",
        "awaiting_user_input" => "Müşteriden bilgi bekleniyor",
        "escalation_needed" => "Temsilciye aktarıldı",
        "not_found" => "Kayıt bulunamadı",
        "error" => "Hata",
        "timeout" => "Zaman aşımı",
        "max_messages_reached" => "Mesaj sınırı aşıldı",
        "repeated_tool_call_guard" => "Tekrarlanan araç çağrısı",
        "budget_exceeded" => "Harcama limiti",
        "cancelled" => "İptal edildi",
        "unknown" => "Bilinmiyor",
        _ => reason
    };

    /// <summary>Başarı oranı rozeti: ≥ %80 iyi, ≥ %50 dikkat, altı kötü.</summary>
    public static string SuccessTone(double? rate) => rate switch
    {
        null => "",
        >= 0.8 => "good",
        >= 0.5 => "warn",
        _ => "bad"
    };

    /// <summary>Araç hata rozeti: hiç hata yoksa sessiz; ≥ %25 kötü, altı dikkat.</summary>
    public static string FailureTone(int failures, double rate) =>
        failures == 0 ? "" : rate >= 0.25 ? "bad" : "warn";
}
