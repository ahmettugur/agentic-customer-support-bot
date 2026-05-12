// Services/InputGuard.cs
// Deterministic input gate — runs BEFORE the user message reaches any LLM.
// Catches obvious abuse vectors that prompt-level guardrails alone cannot reliably stop:
//   - Length DoS / token bomb
//   - Suspicious prompt-injection / jailbreak patterns
//   - Zero-width / RTL Unicode tricks
//   - Excessive token-like ID enumeration (cost bomb)
// Returns an InputGuardResult with a verdict (Allow / Sanitize / Reject) plus reasoning.

using System.Text;
using System.Text.RegularExpressions;

namespace CustomerSupportBot.Services;

public enum InputGuardVerdict
{
    Allow,
    Sanitize,
    Reject
}

public sealed record InputGuardResult(
    InputGuardVerdict Verdict,
    string SanitizedInput,
    IReadOnlyList<string> Flags,
    string? RejectionReason);

public sealed partial class InputGuard
{
    /// <summary>Maksimum kullanıcı mesaj uzunluğu (karakter).</summary>
    public const int MaxInputLength = 2000;

    /// <summary>Maksimum tek mesajda görünebilecek ID sayısı (ORD-/CMP-/CUST-).</summary>
    public const int MaxIdMentions = 8;

    /// <summary>Tehlike sinyali — eşleşince mesaj reddedilir (LLM'e gitmez).</summary>
    private static readonly Regex InjectionPattern = InjectionRegex();

    /// <summary>Yumuşak sinyal — flag'lenir ama mesaj geçer (sanitize edilebilir).</summary>
    private static readonly Regex SoftSuspiciousPattern = SoftSuspiciousRegex();

    /// <summary>HTML/script/img injection — admin paneline veya trace store'a sızma riski.</summary>
    private static readonly Regex HtmlScriptPattern = HtmlScriptRegex();

    /// <summary>ID enumeration (cost bomb) — ORD-1 ORD-2 ... ORD-1000 tarzı.</summary>
    private static readonly Regex IdMentionPattern = IdMentionRegex();

    /// <summary>Zero-width / RTL override / bidi karakterler.</summary>
    private static readonly Regex InvisibleCharPattern = InvisibleCharRegex();

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

        // 6. Yumuşak sinyaller — flag'le ama geçir
        var softMatch = SoftSuspiciousPattern.Match(normalized);
        if (softMatch.Success)
        {
            flags.Add($"soft_suspicious:{softMatch.Value.ToLowerInvariant()}");
            return new InputGuardResult(
                InputGuardVerdict.Sanitize,
                normalized,
                flags,
                null);
        }

        return new InputGuardResult(InputGuardVerdict.Allow, normalized, flags, null);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Regex tanımları (compile-time generated)
    // ─────────────────────────────────────────────────────────────────────

    [GeneratedRegex(
        @"(?ix)
          (ignore\s+(all\s+)?previous|disregard\s+(all\s+)?previous|forget\s+(all\s+)?previous|
           \u00f6nceki\s+t[ua]?l[i\u0131]matlar[i\u0131]?n[i\u0131]?\s+(yok\s+say|unut|g\u00f6z\s*ard\u0131)|
           system\s+prompt|sistem\s+prompt|reveal\s+your|prompt\s+leak|
           you\s+are\s+now|sen\s+art[i\u0131]k|act\s+as\s+(an?\s+)?(admin|developer|root)|
           jailbreak|dan\s+mode|developer\s+mode|admin\s+mode|root\s+access|
           bypass\s+(your|the)\s+(rules|filter|safety)|disable\s+(safety|filter)|
           kurallar[i\u0131]n[i\u0131]?\s+(yok\s+say|unut|de\u011fi\u015ftir)|
           kayd[i\u0131]\s+sil|ba\u015fka(s[i\u0131])?n[i\u0131]n\s+(verisini|hesab[i\u0131]n[i\u0131]?))",
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
        @"\b(ORD|CMP|CUST)-\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex IdMentionRegex();

    [GeneratedRegex(
        @"[\u200B-\u200F\u202A-\u202E\u2060-\u206F\uFEFF]",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex InvisibleCharRegex();
}
