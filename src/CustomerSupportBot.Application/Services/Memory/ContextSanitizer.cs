// Application/Services/Memory/ContextSanitizer.cs
// IContextSanitizer implementasyonu — retrieval içeriğini prompt-injection'a karşı sertleştirir.
// Yazma tarafında Sanitize (Episodic bellek), okuma tarafında WrapRetrieved (KB/Lesson fence) kullanılır.
// Saf ve stateless — singleton kaydedilebilir.

using System.Text;
using System.Text.RegularExpressions;

using CustomerSupportBot.Application.Ports.Outbound;

namespace CustomerSupportBot.Application.Services.Memory;

public sealed partial class ContextSanitizer : IContextSanitizer
{
    // Fence kapanışı içerikte geçerse tek tırnaklı guillemet'e çevrilir — fence kırılamaz.
    private const string ClosingTag = "</retrieved_data>";
    private const string NeutralizedClosingTag = "‹/retrieved_data›";

    public string Sanitize(string text, int maxLength = 2000)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var cleaned = HtmlCommentRegex().Replace(text, string.Empty);
        cleaned = StripControlChars(cleaned);

        if (maxLength > 0 && cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength] + "…";

        return cleaned;
    }

    public string WrapRetrieved(string text, string source)
    {
        var sanitized = Sanitize(text);
        var safe = ClosingTagRegex().Replace(sanitized, NeutralizedClosingTag);
        return $"<retrieved_data source=\"{source}\">{safe}</retrieved_data>";
    }

    private static string StripControlChars(string text)
    {
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            // \n korunur; diğer C0 (0x00-0x1F), DEL (0x7F) ve C1 (0x80-0x9F) kontrolleri atılır.
            var isControl = (c < 0x20 && c != '\n') || (c >= 0x7F && c <= 0x9F);
            if (!isControl) { sb?.Append(c); continue; }
            if (sb is null)
            {
                sb = new StringBuilder(text.Length);
                sb.Append(text, 0, i);
            }
        }
        return sb?.ToString() ?? text;
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"</retrieved_data\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ClosingTagRegex();
}
