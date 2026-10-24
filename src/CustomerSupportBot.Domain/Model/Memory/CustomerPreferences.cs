// Domain/Model/Memory/CustomerPreferences.cs
// Müşterinin AÇIKÇA söylediği kalıcı tercihler. Kapalı sözlük (anahtar × değer): serbest metin saklanmaz — enjeksiyon/PII yüzeyi yoktur.
// Ayrıştırma deterministiktir (LLM yok): yanlış çıkarım kalıcı hâle gelmesin, KVKK kapsamı genişlemesin diye kalıcılık işareti şarttır.

using System.Text;
using System.Text.RegularExpressions;

namespace CustomerSupportBot.Domain.Model.Memory;

public static class CustomerPreferences
{
    public const string ReplyStyle = "reply_style";
    public const string Delivery = "delivery";
    public const string Contact = "contact";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Vocabulary =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [ReplyStyle] = new Dictionary<string, string> { ["short"] = "kısa yanıtlar", ["detailed"] = "ayrıntılı yanıtlar" },
            [Delivery] = new Dictionary<string, string> { ["pickup"] = "mağazadan teslim", ["home"] = "adrese teslim" },
            [Contact] = new Dictionary<string, string> { ["email"] = "e-posta ile iletişim", ["chat"] = "sohbetten iletişim" }
        };

    public static bool IsAllowed(string? key, string? value) =>
        key is not null && value is not null && Vocabulary.TryGetValue(key, out var values) && values.ContainsKey(value);

    /// <summary>"kısa yanıtlar" gibi okunur etiket; sözlükte olmayan giriş boş döner.</summary>
    public static string LabelOf(string key, string value) =>
        Vocabulary.TryGetValue(key, out var values) && values.TryGetValue(value, out var label) ? label : "";

    /// <summary>Profildeki geçerli tercihlerin virgüllü Türkçe özeti; geçersiz/eski girişler atlanır.</summary>
    public static string Describe(IReadOnlyDictionary<string, string> preferences) =>
        string.Join(", ", preferences.Select(p => LabelOf(p.Key, p.Value)).Where(l => l.Length > 0));
}

/// <param name="Key"><c>null</c> = tüm tercihleri sıfırla.</param>
public sealed record PreferenceChange(string? Key, string? Value);

public static partial class PreferenceIntentParser
{
    public static PreferenceChange? Parse(string? text)
    {
        var all = ParseAll(text);
        return all.Count == 1 ? all[0] : null;
    }

    public static IReadOnlyList<PreferenceChange> ParseAll(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var t = Fold(text);

        if (Reset().IsMatch(t)) return [new PreferenceChange(null, null)];
        if (Temporary().IsMatch(t) || !Durable().IsMatch(t)) return [];

        var changes = new List<PreferenceChange>();
        Add(changes, ReplyStyleKey, Pick(t, (Short(), "short"), (Detailed(), "detailed")));
        Add(changes, DeliveryKey, Pick(t, (Pickup(), "pickup"), (Home(), "home")));
        Add(changes, ContactKey, Pick(t, (Email(), "email"), (Chat(), "chat")));
        return changes;
    }

    private const string ReplyStyleKey = CustomerPreferences.ReplyStyle;
    private const string DeliveryKey = CustomerPreferences.Delivery;
    private const string ContactKey = CustomerPreferences.Contact;

    /// <summary>Aynı anahtar için birden çok değer eşleşirse belirsizdir → hiçbiri.</summary>
    private static string? Pick(string t, params (Regex Pattern, string Value)[] options)
    {
        var hits = options.Where(o => o.Pattern.IsMatch(t)).Select(o => o.Value).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    private static void Add(List<PreferenceChange> list, string key, string? value)
    {
        if (value is not null) list.Add(new PreferenceChange(key, value));
    }

    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c switch
            {
                'İ' or 'I' or 'ı' => 'i', 'Ş' or 'ş' => 's', 'Ğ' or 'ğ' => 'g', 'Ü' or 'ü' => 'u', 'Ö' or 'ö' => 'o', 'Ç' or 'ç' => 'c',
                _ => char.ToLowerInvariant(c)
            });
        return sb.ToString();
    }

    [GeneratedRegex(@"\b(bundan\s+(sonra|boyle)|artik|her\s*zaman|hep|daima|surekli)\b")]
    private static partial Regex Durable();

    [GeneratedRegex(@"\b(bu\s+seferlik|simdilik|sadece\s+bu|bir\s+defalik|bu\s+sefer)\b")]
    private static partial Regex Temporary();

    [GeneratedRegex(@"tercih\w*\s+(\w+\s+)?(sifirla|sil|unut|temizle)|(sifirla|sil|unut|temizle)\w*\s+(\w+\s+)?tercih")]
    private static partial Regex Reset();

    [GeneratedRegex(@"\b(kisa|kisaca|ozet)\w*")]
    private static partial Regex Short();

    [GeneratedRegex(@"\b(ayrintili|detayli)\w*")]
    private static partial Regex Detailed();

    [GeneratedRegex(@"\b(magaza|sube)\w*.{0,40}(teslim|al\w*)|\b(teslim|al\w*).{0,40}(magaza|sube)\w*")]
    private static partial Regex Pickup();

    [GeneratedRegex(@"\b(adres|kap[i]|ev)\w*.{0,30}teslim")]
    private static partial Regex Home();

    [GeneratedRegex(@"\b(e-?posta|eposta|mail)\w*.{0,40}(bildir|ulas|yaz|gonder|iletisim)\w*|\b(bildir|ulas|yaz|gonder)\w*.{0,40}\b(e-?posta|eposta|mail)\w*")]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(sohbet|chat)\w*.{0,40}(bildir|ulas|yaz|iletisim)\w*")]
    private static partial Regex Chat();
}
