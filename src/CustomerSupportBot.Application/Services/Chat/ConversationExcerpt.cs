// Application/Services/Chat/ConversationExcerpt.cs
// Onay ve eskalasyon kartlarının konuşma özeti.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Realtime;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Application.Services.Chat;

/// <summary>
/// Temsilci bir kaydı tek cümleden anlayamaz: talep birkaç turda netleşir ve son cümle çoğu zaman yalnızca "Evet" olur. Özet, kayda götüren
/// son mesajlardan kurulur.
///
/// <para>
/// Kayıt ile mesajların yazılma sırası sabit değildir. Onay turun ortasında oluşur, mesajlar tur bitince yazılır (ölçüldü: 2-8 sn sonra);
/// eskalasyon ise turun sonundadır ve mesajlarla neredeyse aynı anda, önce ya da sonra yazılır. Bu yüzden veritabanı kayıt anına göre kesilir
/// ve tetikleyen cümle kayıttaki metinden eklenir. Kesilmese sonraki konuşma özete girer, eklenmese müşterinin asıl cümlesi görünmezdi.
/// </para>
/// </summary>
public static class ConversationExcerpt
{
    public const int MaxMessages = 6;
    public const int MaxTextLength = 500;

    public const string Customer = "customer";
    public const string Assistant = "assistant";

    /// <param name="before">Kayıttan önceki mesajlar, eskiden yeniye.</param>
    /// <param name="triggerText">Kaydı tetikleyen müşteri cümlesi (<c>UserQuery</c>).</param>
    /// <param name="turnMayBeAnswered">
    /// Kayıt turun sonunda oluştuysa cümle, bot yanıtıyla birlikte zaten yazılmış olabilir: yanıttan önceki müşteri cümlesine de bakılır.
    /// </param>
    public static IReadOnlyList<ConversationExcerptLine> Build(
        IReadOnlyList<ConversationMessage> before, string? triggerText, bool turnMayBeAnswered = false)
    {
        var lines = before.Where(IsShowable).Select(ToLine).ToList();

        var trigger = triggerText?.Trim();
        if (!string.IsNullOrEmpty(trigger) && trigger != VoiceTurnPairer.Placeholder
            && !AlreadySaved(lines, trigger, turnMayBeAnswered))
            lines.Add(new ConversationExcerptLine(Customer, Shorten(trigger)));

        return lines.TakeLast(MaxMessages).ToList();
    }

    private static bool IsShowable(ConversationMessage m) =>
        !string.IsNullOrWhiteSpace(m.Text) && m.Text.Trim() != VoiceTurnPairer.Placeholder;

    private static ConversationExcerptLine ToLine(ConversationMessage m)
    {
        var isCustomer = m.Role == ConversationRoles.User;
        var text = m.Text.Trim();
        return new ConversationExcerptLine(
            isCustomer ? Customer : Assistant,
            Shorten(isCustomer ? text : text.Replace("**", "")));
    }

    private static bool AlreadySaved(List<ConversationExcerptLine> lines, string trigger, bool turnMayBeAnswered)
    {
        var lastCustomer = lines.Count > 0 && lines[^1].Speaker == Assistant && turnMayBeAnswered ? lines.Count - 2 : lines.Count - 1;
        return lastCustomer >= 0 && lines[lastCustomer].Speaker == Customer && lines[lastCustomer].Text == Shorten(trigger);
    }

    private static string Shorten(string text) =>
        text.Length <= MaxTextLength ? text : text[..(MaxTextLength - 1)].TrimEnd() + "…";
}
