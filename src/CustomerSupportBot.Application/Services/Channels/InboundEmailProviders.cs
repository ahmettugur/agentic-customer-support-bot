// Application/Services/Channels/InboundEmailProviders.cs
// Sağlayıcı yüklerini (Postmark JSON, SendGrid form) normalize InboundEmail'e çeviren SAF ayrıştırıcılar + gönderici kimlik doğrulaması.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace CustomerSupportBot.Application.Services.Channels;

/// <summary>
/// Gönderici kimliği: "From" başlığı sahte olabilir. Yalnızca gönderen alanıyla HİZALI bir DKIM imzası ya da (zarf göndereni hizalı) SPF geçişi
/// kimlik sayılır — "dkim=pass" tek başına, saldırganın kendi alanından geçen bir imzayı da kapsar.
/// </summary>
public static partial class SenderAuthentication
{
    public static bool IsAligned(string fromEmail, string? authenticationResults)
    {
        var domain = DomainOf(fromEmail);
        if (domain is null || string.IsNullOrWhiteSpace(authenticationResults)) return false;

        foreach (Match m in DkimPass().Matches(authenticationResults))
            if (AlignedDomains(domain, m.Groups[1].Value)) return true;
        foreach (Match m in SpfPass().Matches(authenticationResults))
            if (AlignedDomains(domain, DomainOf(m.Groups[1].Value) ?? m.Groups[1].Value)) return true;
        return false;
    }

    /// <summary>Gevşek (relaxed) hizalama: aynı alan ya da biri diğerinin alt alanı. "notexample.com", "example.com" ile hizalı DEĞİLDİR.</summary>
    public static bool AlignedDomains(string a, string b)
    {
        a = a.Trim().TrimEnd('.').ToLowerInvariant();
        b = b.Trim().TrimEnd('.').ToLowerInvariant();
        return a.Length > 0 && b.Length > 0 && (a == b || a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal));
    }

    public static string? DomainOf(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at > 0 && at < email!.Length - 1 ? email[(at + 1)..].Trim().ToLowerInvariant() : null;
    }

    /// <summary>"Ayşe &lt;ayse@example.com&gt;" ya da "ayse@example.com" → adres.</summary>
    public static string? AddressOf(string? from)
    {
        if (string.IsNullOrWhiteSpace(from)) return null;
        var m = Address().Match(from);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"dkim=pass\b[^;]*?header\.d=([^\s;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DkimPass();

    [GeneratedRegex(@"spf=pass\b[^;]*?smtp\.mailfrom=([^\s;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SpfPass();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+")]
    private static partial Regex Address();
}

/// <summary>Postmark inbound webhook (JSON).</summary>
public static class PostmarkInbound
{
    public static InboundEmail? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        var from = Str(root, "FromFull", "Email") ?? SenderAuthentication.AddressOf(Str(root, "From"));
        if (string.IsNullOrWhiteSpace(from)) return null;
        from = from.Trim().ToLowerInvariant();

        string? authResults = null, autoSubmitted = null;
        if (root.TryGetProperty("Headers", out var headers) && headers.ValueKind == JsonValueKind.Array)
            foreach (var h in headers.EnumerateArray())
            {
                var name = Str(h, "Name");
                var value = Str(h, "Value");
                if (name is null || value is null) continue;
                if (name.Equals("Authentication-Results", StringComparison.OrdinalIgnoreCase)) authResults = authResults is null ? value : authResults + "; " + value;
                else if (name.Equals("Auto-Submitted", StringComparison.OrdinalIgnoreCase)) autoSubmitted = value;
            }

        var attachments = new List<InboundAttachment>();
        if (root.TryGetProperty("Attachments", out var atts) && atts.ValueKind == JsonValueKind.Array)
            foreach (var a in atts.EnumerateArray())
            {
                var content = Str(a, "Content");
                if (content is null) continue;
                try { attachments.Add(new InboundAttachment(Str(a, "Name") ?? "", Str(a, "ContentType") ?? "", Convert.FromBase64String(content))); }
                catch (FormatException) { /* bozuk ek yok sayılır; e-posta metni yine işlenir */ }
            }

        return new InboundEmail(from, Str(root, "Subject"), Str(root, "TextBody"), Str(root, "MessageID"),
            SenderAuthentication.IsAligned(from, authResults), autoSubmitted, attachments);
    }

    private static string? Str(JsonElement e, params string[] path)
    {
        foreach (var p in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}

/// <summary>SendGrid Inbound Parse (multipart/form-data; alanlar sözlük olarak verilir).</summary>
public static partial class SendGridInbound
{
    public static InboundEmail? Parse(IReadOnlyDictionary<string, string> fields, IReadOnlyList<InboundAttachment> files)
    {
        var from = fields.TryGetValue("from", out var f) ? SenderAuthentication.AddressOf(f) : null;
        if (from is null) return null;

        var headers = fields.GetValueOrDefault("headers") ?? "";
        var messageId = HeaderValue(headers, "Message-ID");
        var autoSubmitted = HeaderValue(headers, "Auto-Submitted");

        return new InboundEmail(from, fields.GetValueOrDefault("subject"), fields.GetValueOrDefault("text"), messageId,
            IsAuthenticated(from, fields), autoSubmitted, files);
    }

    private static bool IsAuthenticated(string from, IReadOnlyDictionary<string, string> fields)
    {
        var domain = SenderAuthentication.DomainOf(from);
        if (domain is null) return false;

        // dkim: "{@example.com : pass, @other.com : fail}"
        foreach (Match m in DkimEntry().Matches(fields.GetValueOrDefault("dkim") ?? ""))
            if (m.Groups[2].Value.Equals("pass", StringComparison.OrdinalIgnoreCase) && SenderAuthentication.AlignedDomains(domain, m.Groups[1].Value))
                return true;

        // SPF yalnızca zarf göndereni de hizalıysa.
        if ((fields.GetValueOrDefault("SPF") ?? "").Trim().Equals("pass", StringComparison.OrdinalIgnoreCase)
            && TryEnvelopeFrom(fields.GetValueOrDefault("envelope"), out var envelopeFrom)
            && SenderAuthentication.DomainOf(envelopeFrom) is { } envDomain && SenderAuthentication.AlignedDomains(domain, envDomain))
            return true;
        return false;
    }

    private static bool TryEnvelopeFrom(string? json, out string? from)
    {
        from = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String)
                from = f.GetString();
        }
        catch (JsonException) { }
        return from is not null;
    }

    private static string? HeaderValue(string rawHeaders, string name)
    {
        var m = Regex.Match(rawHeaders, $@"^{Regex.Escape(name)}:\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"@([A-Za-z0-9.\-]+)\s*:\s*(\w+)")]
    private static partial Regex DkimEntry();
}
