// Application/Services/Chat/OutputGuardrail.cs
// Deterministic output gate — runs on the turn's canonical answer BEFORE it reaches the customer or the history.
// Complements InputGuard: prompts alone cannot reliably stop a model from echoing what it was shown.
//   - e-mail / phone that is neither the customer's own, nor typed by them, nor an allow-listed support contact → masked
//   - secrets (JWT, API keys, connection strings) and system-prompt leakage → whole answer replaced

using System.Text;
using System.Text.RegularExpressions;

namespace CustomerSupportBot.Application.Services.Chat;

public enum OutputGuardVerdict { Clean, Redacted, Blocked }

/// <param name="UserTexts">Bu konuşmada MÜŞTERİNİN yazdığı metinler (bot mesajları dahil değil: botun söylediği, müşterinin verdiği anlamına gelmez).</param>
/// <param name="OwnEmail">Doğrulanmış müşterinin kayıtlı e-postası.</param>
public sealed record OutputGuardContext(
    IReadOnlyCollection<string> UserTexts,
    string? OwnEmail,
    IReadOnlyCollection<string> AllowedContacts,
    IReadOnlyCollection<string> BlockedPhrases);

public sealed record OutputGuardResult(string Text, OutputGuardVerdict Verdict, IReadOnlyList<string> Findings);

public static partial class OutputGuardrail
{
    public const string RedactedMark = "[gizlendi]";

    public const string BlockedReply =
        "Bu yanıtı güvenli bir şekilde paylaşamıyorum. Dilerseniz sizi bir temsilciye bağlayabilirim.";

    private static readonly string[] PromptLeakMarkers =
    [
        "system prompt", "sistem prompt", "sistem talimat", "talimatlarim", "my instructions", "my system instructions"
    ];

    public static OutputGuardResult Inspect(string text, OutputGuardContext ctx)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(text, OutputGuardVerdict.Clean, []);

        var findings = new List<string>();

        if (SecretRegex().IsMatch(text)) findings.Add("secret");
        var folded = Fold(text);
        if (PromptLeakMarkers.Any(folded.Contains)) findings.Add("prompt_leak");
        if (ctx.BlockedPhrases.Any(p => !string.IsNullOrWhiteSpace(p) && folded.Contains(Fold(p)))) findings.Add("blocked_phrase");
        if (findings.Count > 0) return new(BlockedReply, OutputGuardVerdict.Blocked, findings);

        var knownEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPhones = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(ctx.OwnEmail)) knownEmails.Add(ctx.OwnEmail.Trim());
        foreach (var t in ctx.UserTexts) Collect(t, knownEmails, knownPhones);
        foreach (var c in ctx.AllowedContacts) Collect(c, knownEmails, knownPhones);

        var redacted = EmailRegex().Replace(text, m =>
        {
            if (knownEmails.Contains(m.Value)) return m.Value;
            findings.Add("email");
            return RedactedMark;
        });
        redacted = PhoneRegex().Replace(redacted, m =>
        {
            if (!LooksLikePhone(m.Value)) return m.Value;
            if (knownPhones.Contains(Last10(m.Value))) return m.Value;
            findings.Add("phone");
            return RedactedMark;
        });

        return findings.Count == 0
            ? new(text, OutputGuardVerdict.Clean, [])
            : new(redacted, OutputGuardVerdict.Redacted, findings.Distinct().ToList());
    }

    private static void Collect(string source, HashSet<string> emails, HashSet<string> phones)
    {
        foreach (Match m in EmailRegex().Matches(source)) emails.Add(m.Value);
        foreach (Match m in PhoneRegex().Matches(source)) phones.Add(Last10(m.Value));
        // İzinli iletişim listesi serbest biçimde ("0850 123 45 67") girilebilir; tam eşleşmeyen rakam dizilerini de al.
        var digits = Digits(source);
        if (digits.Length is >= 10 and <= 13 && !source.Contains('@')) phones.Add(digits[^10..]);
    }

    private static string Digits(string s) => new(s.Where(char.IsAsciiDigit).ToArray());

    private static string Last10(string s)
    {
        var d = Digits(s);
        return d.Length > 10 ? d[^10..] : d;
    }

    /// <summary>Ayraçsız ve 0/+90 önekisiz 10 haneli diziler takip numarası olabilir; telefon sayılmaz.</summary>
    private static bool LooksLikePhone(string match)
    {
        var d = Digits(match);
        if (d.Length >= 11) return true;                     // 0 5xx… / 90 5xx…
        return match.Any(c => c is ' ' or '.' or '-' or '(' or ')');
    }

    /// <summary>Büyük/küçük harf + Türkçe İ/ı farkını eler (yalnızca ifade karşılaştırması için).</summary>
    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c switch { 'İ' or 'I' or 'ı' => 'i', _ => char.ToLowerInvariant(c) });
        return sb.ToString();
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<![\w+])(?:\+?90[\s.\-]*)?\(?0?[\s.\-]*\(?[2-5]\d{2}\)?[\s.\-]*\d{3}[\s.\-]*\d{2}[\s.\-]*\d{2}(?!\d)")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(
        @"eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}"
        + @"|\bsk-[A-Za-z0-9_\-]{20,}"
        + @"|\bBearer\s+[A-Za-z0-9._\-]{20,}"
        + @"|\b(?:Password|Pwd|AccountKey|SharedAccessKey|api[_\-]?key|client[_\-]?secret)\s*=\s*\S+",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretRegex();
}
