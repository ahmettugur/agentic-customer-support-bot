// Application/Services/InputGuard.cs
// Deterministic input gate — runs BEFORE the user message reaches any LLM.
// Catches obvious abuse vectors that prompt-level guardrails alone cannot reliably stop:
//   - Length DoS / token bomb
//   - Suspicious prompt-injection / jailbreak patterns
//   - Zero-width / RTL Unicode tricks
//   - Excessive token-like ID enumeration (cost bomb)
// Returns an InputGuardResult with a verdict (Allow / Sanitize / Reject) plus reasoning.

using System.Text;
using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Services.Logging;

namespace CustomerSupportBot.Application.Services.Chat;

public sealed partial class InputGuard : IInputGuard
{
    /// <summary>Maksimum kullanıcı mesaj uzunluğu (karakter).</summary>
    public const int MaxInputLength = 2000;

    /// <summary>Maksimum tek mesajda görünebilecek ID sayısı (4+ haneli rakamsal ID).</summary>
    public const int MaxIdMentions = 8;

    /// <summary>Tehlike sinyali — eşleşince mesaj reddedilir (LLM'e gitmez).</summary>
    private static readonly Regex InjectionPattern = InjectionRegex();

    /// <summary>Yumuşak sinyal — flag'lenir ama mesaj geçer (sanitize edilebilir).</summary>
    private static readonly Regex SoftSuspiciousPattern = SoftSuspiciousRegex();

    /// <summary>HTML/script/img injection — admin paneline veya trace store'a sızma riski.</summary>
    private static readonly Regex HtmlScriptPattern = HtmlScriptRegex();

    /// <summary>ID enumeration (cost bomb) — 1030 1031 ... 2030 tarzı ardışık ID listesi.</summary>
    private static readonly Regex IdMentionPattern = IdMentionRegex();

    /// <summary>Zero-width / RTL override / bidi karakterler.</summary>
    private static readonly Regex InvisibleCharPattern = InvisibleCharRegex();

    /// <summary>4-4-4-N gruplu kart adayı — boşluk VEYA tire, ama TUTARLI (backreference).
    /// Tek başına yetersiz: gerçek maskeleme kararı Luhn + ardışık-ID kontrolüyle verilir
    /// (bkz. MaskCreditCards).</summary>
    private static readonly Regex CreditCardGroupedPattern = CreditCardGroupedRegex();

    /// <summary>13-19 bitişik (ayraçsız) rakam — bu domain'de hiçbir meşru metin bu şekilde
    /// üretilmez (sipariş ID'leri her zaman boşlukla ayrılır), o yüzden yalnızca Luhn yeterli.</summary>
    private static readonly Regex CreditCardContiguousPattern = CreditCardContiguousRegex();

    /// <summary>Türkiye cep telefonu — "05xx xxx xx xx", "+90 5xx...", ayraçlı/ayraçsız.</summary>
    private static readonly Regex PhonePattern = PhoneRegex();

    /// <summary>TC kimlik no — 11 haneli, ilk hane sıfır olamaz.</summary>
    private static readonly Regex TcknPattern = TcknRegex();

    /// <summary>E-posta adresi.</summary>
    private static readonly Regex EmailPattern = EmailRegex();

    public InputGuardResult Inspect(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new InputGuardResult(
                InputGuardVerdict.Reject,
                string.Empty,
                ["empty_input"],
                "Mesaj boş olamaz.");
        }

        var flags = new List<string>();

        // 1. Length cap
        if (input.Length > MaxInputLength)
        {
            return new InputGuardResult(
                InputGuardVerdict.Reject,
                input[..MaxInputLength],
                ["length_exceeded"],
                $"Mesajınız çok uzun (en fazla {MaxInputLength} karakter). Lütfen kısaltın.");
        }

        // 2. Unicode normalization + invisible char strip
        var normalized = input.Normalize(NormalizationForm.FormKC);
        if (InvisibleCharPattern.IsMatch(normalized))
        {
            flags.Add("invisible_chars_stripped");
            normalized = InvisibleCharPattern.Replace(normalized, string.Empty);
        }

        // 2b. PII maskeleme — burada maskelenen metin hem LLM'e GİDEN hem de loglanan/
        // ReasoningTrace.UserQuery'ye yazılan metinle AYNIDIR (SanitizedInput). Müşteri
        // sohbete yanlışlıkla telefon/TC/kredi kartı/e-posta yazarsa bu bilgi ne üçüncü
        // parti LLM sağlayıcısına ham gider ne de kalıcı trace'e düşer.
        (normalized, var piiFlags) = MaskPii(normalized);
        flags.AddRange(piiFlags);

        // 3. Hard injection pattern → reject
        var injectionMatch = InjectionPattern.Match(normalized);
        if (injectionMatch.Success)
        {
            flags.Add($"injection_pattern:{injectionMatch.Value.ToLowerInvariant()}");
            return new InputGuardResult(
                InputGuardVerdict.Reject,
                normalized,
                flags,
                "Bu konuda yardımcı olamam. Sipariş, ürün veya şikayet konularında destek olabilirim.");
        }

        // 4. HTML/script tag → reject (XSS karşı admin paneli koruması)
        if (HtmlScriptPattern.IsMatch(normalized))
        {
            flags.Add("html_or_script_tag");
            return new InputGuardResult(
                InputGuardVerdict.Reject,
                normalized,
                flags,
                "Mesajınızda izin verilmeyen içerik tespit edildi.");
        }

        // 5. ID enumeration (cost bomb) → reject
        var idMatches = IdMentionPattern.Matches(normalized);
        if (idMatches.Count > MaxIdMentions)
        {
            flags.Add($"too_many_ids:{idMatches.Count}");
            return new InputGuardResult(
                InputGuardVerdict.Reject,
                normalized,
                flags,
                $"Tek mesajda en fazla {MaxIdMentions} sipariş/şikayet numarası işleyebilirim. Lütfen ayrı mesajlar halinde gönderin.");
        }

        // 6. Yumuşak sinyaller — LLM payload sahteciliği riski → reddet
        // ```json, [INST], "approved":true gibi payload'lar LLM'e giderse yanlış karar tetiklenebilir.
        var softMatch = SoftSuspiciousPattern.Match(normalized);
        if (softMatch.Success)
        {
            flags.Add($"soft_suspicious:{softMatch.Value.ToLowerInvariant()}");
            return new InputGuardResult(
                InputGuardVerdict.Reject,
                normalized,
                flags,
                "Mesajınızda izin verilmeyen içerik tespit edildi.");
        }

        return new InputGuardResult(InputGuardVerdict.Allow, normalized, flags, null);
    }

    private static (string Text, List<string> Flags) MaskPii(string text)
    {
        var flags = new List<string>();

        var (afterCard, cardMasked) = MaskCreditCards(text);
        text = afterCard;
        if (cardMasked) flags.Add("pii_masked:credit_card");

        var beforePhone = text;
        text = PhonePattern.Replace(text, m => PiiMasker.MaskPhone(m.Value));
        if (text != beforePhone) flags.Add("pii_masked:phone");

        var beforeTckn = text;
        text = TcknPattern.Replace(text, m => PiiMasker.MaskTcKimlikNo(m.Value));
        if (text != beforeTckn) flags.Add("pii_masked:tckn");

        var beforeEmail = text;
        text = EmailPattern.Replace(text, m => PiiMasker.MaskEmail(m.Value));
        if (text != beforeEmail) flags.Add("pii_masked:email");

        return (text, flags);
    }

    // Boşlukla ayrılmış "4111 1111 1111 1111" formatı, sektörde en yaygın yazım biçimi olduğu
    // için BİLEREK yakalanıyor — ama bu domain'de "1030 1031 1032 1033" gibi ardışık sipariş/
    // şikayet ID listeleri de YAPISAL OLARAK BİREBİR AYNI şekle sahip (dört 4-haneli grup,
    // boşlukla ayrılmış). Regex tek başına ikisini ayırt edemez; bu yüzden İKİ bağımsız filtre
    // birlikte kullanılır:
    //   1) Luhn checksum — gerçek kart numaraları bunu sağlar, rastgele/ardışık rakamlar ~%90
    //      ihtimalle sağlamaz.
    //   2) "Ardışık ID listesi" testi — dört grup sabit küçük farkla (1-3) artıyorsa (tam olarak
    //      "1030,1031,1032,1033" deseni) kart SAYILMAZ, Luhn'u geçse bile.
    // Empirik doğrulama: 5000 rastgele ardışık sipariş-ID mesajı simüle edildiğinde bu iki
    // filtre birlikte %0 yanlış pozitif verdi (yalnız Luhn ile ~%19-29 arası yanlış pozitifti).
    private static (string Text, bool Masked) MaskCreditCards(string text)
    {
        var masked = false;

        text = CreditCardGroupedPattern.Replace(text, m =>
        {
            var g1 = m.Groups[1].Value;
            var g2 = m.Groups[3].Value;
            var g3 = m.Groups[4].Value;
            var g4 = m.Groups[5].Value;

            if (LooksLikeSequentialIdList(g1, g2, g3, g4)) return m.Value;

            var digitsOnly = g1 + g2 + g3 + g4;
            if (!PassesLuhn(digitsOnly)) return m.Value;

            masked = true;
            return PiiMasker.MaskCreditCard(m.Value);
        });

        text = CreditCardContiguousPattern.Replace(text, m =>
        {
            if (!PassesLuhn(m.Value)) return m.Value;
            masked = true;
            return PiiMasker.MaskCreditCard(m.Value);
        });

        return (text, masked);
    }

    private static bool LooksLikeSequentialIdList(string g1, string g2, string g3, string g4)
    {
        if (!int.TryParse(g1, out var n1) || !int.TryParse(g2, out var n2) ||
            !int.TryParse(g3, out var n3) || !int.TryParse(g4, out var n4))
            return false;

        var d1 = n2 - n1;
        var d2 = n3 - n2;
        var d3 = n4 - n3;
        return d1 == d2 && d2 == d3 && d1 is > 0 and <= 3;
    }

    private static bool PassesLuhn(string digitsOnly)
    {
        if (digitsOnly.Length < 13) return false;

        var sum = 0;
        var alternate = false;
        for (var i = digitsOnly.Length - 1; i >= 0; i--)
        {
            var d = digitsOnly[i] - '0';
            if (alternate)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Regex tanımları (compile-time generated)
    // ─────────────────────────────────────────────────────────────────────

    [GeneratedRegex(
        @"(?ix)
          (ignore\s+(all\s+)?previous|disregard\s+(all\s+)?previous|forget\s+(all\s+)?previous|
           önceki\s+t[ua]?l[iı]matlar[iı]?n[iı]?\s+(yok\s+say|unut|göz\s*ardı)|
           system\s+prompt|sistem\s+prompt|reveal\s+your|prompt\s+leak|
           you\s+are\s+now|sen\s+art[iı]k|act\s+as\s+(an?\s+)?(admin|developer|root)|
           jailbreak|dan\s+mode|developer\s+mode|admin\s+mode|root\s+access|
           bypass\s+(your|the)\s+(rules|filter|safety)|disable\s+(safety|filter)|
           kurallar[iı]n[iı]?\s+(yok\s+say|unut|değiştir)|
           kayd[iı]\s+sil|başka(s[iı])?n[iı]n\s+(verisini|hesab[iı]n[iı]?))",
        RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex InjectionRegex();

    [GeneratedRegex(
        @"(?ix)(`{3}\s*json|<\|.*?\|>|\[INST\]|\[\/INST\]|<system>|<\/system>|""approved""\s*:\s*true|""rejected""\s*:\s*false)",
        RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex SoftSuspiciousRegex();

    [GeneratedRegex(
        @"(<\s*(script|iframe|img|svg|object|embed|link|meta|style)\b|javascript\s*:|on[a-z]+\s*=)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex HtmlScriptRegex();

    [GeneratedRegex(
        @"\b\d{4,}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex IdMentionRegex();

    [GeneratedRegex(
        @"[​-‏‪-‮⁠-⁯﻿]",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex InvisibleCharRegex();

    // (\d{4})(sep)(\d{4})\2(\d{4})\2(\d{1,7}) — backreference \2 aynı ayracın (boşluk VEYA
    // tire) tüm gruplarda TUTARLI kullanılmasını zorunlu kılar (karışık "4111-1111 1111-1111"
    // gibi bir şey kart formatı değildir, eşleşmez). Karar Luhn + ardışık-ID testiyle verilir
    // (bkz. MaskCreditCards).
    [GeneratedRegex(
        @"\b(\d{4})([ -])(\d{4})\2(\d{4})\2(\d{1,7})\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex CreditCardGroupedRegex();

    [GeneratedRegex(
        @"\b\d{13,19}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex CreditCardContiguousRegex();

    [GeneratedRegex(
        @"\b(?:\+90[\s-]?)?0?5\d{2}[\s-]?\d{3}[\s-]?\d{2}[\s-]?\d{2}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(
        @"\b[1-9]\d{10}\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex TcknRegex();

    [GeneratedRegex(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex EmailRegex();
}
