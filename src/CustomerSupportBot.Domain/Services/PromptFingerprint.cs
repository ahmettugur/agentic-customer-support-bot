// Domain/Services/PromptFingerprint.cs
// Prompt içerik sürümü. Elle sürüm numarası unutulur ya da yanlış artırılır; içerikten türeyen sürüm
// her düzenlemede kendiliğinden değişir. Bir yanıtın hangi prompt metniyle üretildiği trace'ten okunur.

using System.Security.Cryptography;
using System.Text;

namespace CustomerSupportBot.Domain.Services;

public static class PromptFingerprint
{
    /// <summary>Bir prompt'un içerik sürümü: SHA-256'nın ilk 8 hex karakteri. Satır sonu farkı (CRLF/LF) sayılmaz.</summary>
    public static string Of(string content) => Hash(content.Replace("\r\n", "\n"));

    /// <summary>
    /// Tüm prompt'ların birleşik sürümü (anahtar + sürüm çiftlerinden). Sıradan bağımsızdır; herhangi bir prompt
    /// düzenlenir, eklenir, silinir ya da yeniden adlandırılırsa değişir. Trace'e ve Langfuse'a bu damgalanır.
    /// </summary>
    public static string SetVersion(IReadOnlyDictionary<string, string> versionsByKey)
    {
        var canonical = string.Join('\n',
            versionsByKey.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
        return Hash(canonical);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();
}
