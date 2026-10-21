namespace CustomerSupportBot.Web.Services;

/// <summary>Bot yanıtı altındaki 👍/👎 çubuğunun saf kuralları (bileşenden ayrı tutulur, test edilebilir).</summary>
public static class FeedbackUi
{
    /// <summary>Sunucunun kapalı neden kümesi (<c>MessageFeedbackReasons</c>) ve arayüz etiketleri.</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Reasons =
    [
        ("wrong", "Yanlış bilgi"),
        ("irrelevant", "Sorumu yanıtlamadı"),
        ("unclear", "Anlaşılmadı"),
        ("other", "Diğer")
    ];

    /// <summary>Aynı düğmeye tekrar basmak oyu geri çeker (null); farklı düğme oyu değiştirir.</summary>
    public static bool? NextVote(bool? current, bool pressed) => current == pressed ? null : pressed;

    /// <summary>Neden seçenekleri yalnızca 👎'den sonra, henüz bir neden seçilmediyse gösterilir.</summary>
    public static bool ShowReasons(bool? vote, string? reason) => vote == false && string.IsNullOrEmpty(reason);

    /// <summary>
    /// Yalnızca tamamlanmış BOT yanıtına oy verilir: akan, boş, temsilci/kullanıcı ve sesli görüşme balonlarına değil;
    /// sunucu da yalnızca oturum geçmişindeki bot mesajını kabul eder, oturum yoksa gönderilecek yer yoktur.
    /// </summary>
    public static bool CanVote(string role, bool isStreaming, string content, string? sessionId, bool isVoiceBubble) =>
        role == "assistant" && !isStreaming && !string.IsNullOrWhiteSpace(content) && !string.IsNullOrEmpty(sessionId) && !isVoiceBubble;
}
