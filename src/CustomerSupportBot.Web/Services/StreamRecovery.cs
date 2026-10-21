namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Akış koptuğunda (ağ, proxy zaman aşımı) ya da sunucu hata olayı yolladığında, tur sunucuda YİNE DE tamamlanıp kaydedildiyse
/// yanıtı oturum geçmişinden alır. Yanlış pozitif ("eski bir yanıtı yeni sanmak") en kötü sonuçtur: eşleşme sıkıdır ve
/// yeniliği BAŞLANGIÇ mesaj sayısına göre doğrular — müşteri aynı soruyu daha önce de sormuş olabilir.
/// </summary>
public static class StreamRecovery
{
    /// <param name="serverMessages">Oturumun sunucudaki güncel mesajları.</param>
    /// <param name="baselineCount">Bu tur gönderilmeden ÖNCE sunucudaki mesaj sayısı.</param>
    /// <param name="sentText">Gönderilen kullanıcı metni.</param>
    /// <returns>Tamamlanmış turun bot yanıtı; emin değilse <c>null</c>.</returns>
    public static string? FindCompletedAnswer(IReadOnlyList<SessionMessage> serverMessages, int baselineCount, string sentText)
    {
        // Bu tur en az (kullanıcı + bot) iki yeni mesaj eklemiş olmalı.
        if (serverMessages.Count < baselineCount + 2) return null;

        var bot = serverMessages[^1];
        var user = serverMessages[^2];
        if (bot.Role == "user" || user.Role != "user") return null;
        if (string.IsNullOrWhiteSpace(bot.Text)) return null;

        return Normalize(user.Text) == Normalize(sentText) ? bot.Text : null;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
