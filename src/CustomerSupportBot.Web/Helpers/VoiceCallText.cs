// Helpers/VoiceCallText.cs — sesli görüşme durum metinleri (temsilci ve müşteri arayüzü ortak).

namespace CustomerSupportBot.Web.Helpers;

public static class VoiceCallText
{
    public static string StatusLabel(string status, string? endReason) => status switch
    {
        "ringing" => "Aranıyor…",
        "active" => "Görüşmede",
        "declined" when endReason == "no_microphone" => "Müşterinin mikrofonu yok",
        "declined" => "Müşteri reddetti",
        "missed" => "Cevap verilmedi",
        "cancelled" => "Arama iptal edildi",
        "failed" => "Bağlantı koptu",
        _ => "Görüşme bitti"
    };

    public static string FormatElapsed(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    public static string StartError(string? code) => code switch
    {
        "voice_call_busy" => "Zaten bir sesli görüşmedesiniz.",
        "not_in_human_mode" => "Sesli görüşme için önce sohbeti devralın.",
        "disabled" => "Sesli görüşme şu anda kapalı.",
        _ => "Sesli görüşme başlatılamadı."
    };
}
