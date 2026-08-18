// Application/Services/A2A/A2ASubjectIdentity.cs
// Özne token'ının kimlik biçimi — TEK tanım yeri.

namespace CustomerSupportBot.Application.Services.A2A;

/// <summary>
/// Değişimle üretilen özne token'ının kimlik (<c>sub</c>) biçimini kuran ve çözen tek yer.
///
/// <para>
/// <b>Neden tek yerde:</b> bu biçim iki yerde kullanılıyor — token üretilirken
/// (<see cref="A2ATokenExchangeService"/>) ve rate limit bölümlemesinde partner çıkarılırken.
/// İki yerde ayrı ayrı elle yazılsaydı, biri değiştiğinde rate limit sessizce yanlış anahtara
/// bölümler ve <b>partner başına sınır fiilen ortadan kalkardı</b> — hata görünür bir
/// arıza değil, sessizce kaybolan bir koruma olurdu.
/// </para>
/// </summary>
public static class A2ASubjectIdentity
{
    private const string Prefix = "a2a";
    private const char Separator = ':';

    /// <summary>Özne token'ının kimliği: <c>a2a:{partnerId}:{customerId}</c>.</summary>
    public static string BuildId(string partnerId, string customerId)
        => $"{Prefix}{Separator}{partnerId}{Separator}{customerId}";

    /// <summary>
    /// Kimlikten partner'ı çıkarır. Biçim beklenenden farklıysa <c>null</c> döner —
    /// çağıran bunu "bilinmeyen partner" olarak ele almalı, tahmin etmemelidir.
    /// </summary>
    public static string? TryGetPartnerId(string? subjectId)
    {
        if (string.IsNullOrWhiteSpace(subjectId)) return null;

        var parts = subjectId.Split(Separator);
        if (parts.Length < 3) return null;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return null;

        return string.IsNullOrWhiteSpace(parts[1]) ? null : parts[1];
    }
}
