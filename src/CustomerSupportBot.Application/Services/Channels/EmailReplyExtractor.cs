// Application/Services/Channels/EmailReplyExtractor.cs
// E-posta yanıtından yalnızca müşterinin YENİ yazdığı kısmı çıkarır ve otomatik postayı tanır (saf).

using System.Text.RegularExpressions;

namespace CustomerSupportBot.Application.Services.Channels;

public static partial class EmailReplyExtractor
{
    public static string ExtractNewText(string? raw, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var next = i + 1 < lines.Length ? lines[i + 1] : "";

            if (IsQuoteBoundary(line, next)) break;
            if (line.TrimStart().StartsWith('>')) continue;   // başlıksız alıntı satırları
            kept.Add(line);
        }

        var text = string.Join('\n', kept).Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    /// <summary>Otomatik yanıt / geri dönen posta / bir sistem adresi mi? Bunlara yanıt vermek posta döngüsü başlatır.</summary>
    public static bool IsAutomated(string from, string? subject, string? autoSubmitted)
    {
        if (!string.IsNullOrWhiteSpace(autoSubmitted) && !autoSubmitted.Trim().Equals("no", StringComparison.OrdinalIgnoreCase))
            return true;
        if (AutomatedSender().IsMatch(from ?? "")) return true;
        return !string.IsNullOrWhiteSpace(subject) && AutomatedSubject().IsMatch(subject);
    }

    private static bool IsQuoteBoundary(string line, string next)
    {
        var t = line.Trim();
        if (t.Length == 0) return false;
        if (t == "--") return true;                                   // imza ayracı ("-- ")
        if (QuoteHeaderEn().IsMatch(t) || QuoteHeaderTr().IsMatch(t)) return true;
        // "On … wrote:" bazı istemcilerde iki satıra bölünür.
        if (t.StartsWith("On ", StringComparison.Ordinal) && next.Trim().EndsWith("wrote:", StringComparison.Ordinal)) return true;
        if (Separator().IsMatch(t)) return true;
        if (SentFrom().IsMatch(t)) return true;
        return false;
    }

    [GeneratedRegex(@"^On .+ wrote:$")]
    private static partial Regex QuoteHeaderEn();

    [GeneratedRegex(@"^.+ tarihinde .+ (şunu )?yazdı:$", RegexOptions.IgnoreCase)]
    private static partial Regex QuoteHeaderTr();

    [GeneratedRegex(@"^(-{2,}\s*(Original Message|Orijinal Mesaj|Forwarded message|İletilen mesaj)\s*-{2,}|_{10,})$", RegexOptions.IgnoreCase)]
    private static partial Regex Separator();

    [GeneratedRegex(@"^(Sent from my |Gönderen: iPhone|iPhone'umdan gönderildi|Outlook for )", RegexOptions.IgnoreCase)]
    private static partial Regex SentFrom();

    [GeneratedRegex(@"(^|[.\-_+])(no-?reply|do-?not-?reply|mailer-daemon|postmaster)@", RegexOptions.IgnoreCase)]
    private static partial Regex AutomatedSender();

    [GeneratedRegex(@"^\s*(out of office|automatic reply|auto(matic)?[ -]?reply|autoreply|otomatik yanıt|ofis dışında|undeliverable|undelivered|delivery status)", RegexOptions.IgnoreCase)]
    private static partial Regex AutomatedSubject();
}
