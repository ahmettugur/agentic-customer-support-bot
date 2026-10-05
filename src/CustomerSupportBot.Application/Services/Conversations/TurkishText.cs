// Application/Services/Conversations/TurkishText.cs
// Türkçe metin normalleştirme: etiketler ve arama katlaması.

using System.Globalization;
using System.Text.RegularExpressions;

namespace CustomerSupportBot.Application.Services.Conversations;

public static partial class TurkishText
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Arama katlaması: <c>İ I ı → i</c>, ardından Türkçe küçük harf. Karakter karakter yapılır, uzunluk
    /// korunur (eşleşmenin konumu özgün metinde de geçerlidir). SQL tarafı aynısını
    /// <c>lower(translate(text, 'İIıÇĞÖŞÜ', 'iiiçğöşü'))</c> ile yapar — veritabanının yerel ayarına
    /// ('C' yerelinde <c>lower()</c> yalnızca ASCII'yi çevirir) bağlı kalmamak için Türkçe büyük harfler
    /// açıkça çevrilir. "ı" ile "i" bilinçli olarak aynı sayılır.
    /// </summary>
    public static string Fold(string text)
    {
        var chars = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            chars[i] = c is 'İ' or 'I' or 'ı' ? 'i' : char.ToLower(c, Turkish);
        }
        return new string(chars);
    }

    /// <summary>
    /// Etiket: küçük harf (Türkçe), boşluklar <c>-</c>. <c>I</c> ve <c>İ</c> önce <c>i</c>'ye çevrilir: yalnızca
    /// Türkçe küçük harf "IADE"yi "ıade" yapar ve "iade" ile ayrı etiket sayılırdı.
    /// </summary>
    public static string NormalizeTag(string? raw) =>
        Whitespace().Replace((raw ?? "").Trim().Replace('I', 'i').Replace('İ', 'i').ToLower(Turkish), "-");
}
