// Application/Services/Logging/PiiMasker.cs
// Log satırlarına yazılmadan önce PII (e-posta, telefon, TC kimlik no, kredi kartı, IP)
// alanlarını maskeleyen paylaşımlı yardımcı — CustomerAuthService.MaskEmail'in genişletilmiş hâli.

using System.Text;

namespace CustomerSupportBot.Application.Services.Logging;

public static class PiiMasker
{
    /// <summary>İlk karakter + domain korunur, geri kalan local-part maskelenir: "ahmet@x.com" → "a***@x.com".</summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return email ?? "";

        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }

    /// <summary>Son 2 hane korunur, formatlama (boşluk/tire/+) korunarak diğer haneler maskelenir.</summary>
    public static string MaskPhone(string? phone) => MaskDigitsKeepTail(phone, visibleTailDigits: 2);

    /// <summary>TC kimlik no — son 2 hane korunur (11 hanenin tamamı loglanmaz).</summary>
    public static string MaskTcKimlikNo(string? tckn) => MaskDigitsKeepTail(tckn, visibleTailDigits: 2);

    /// <summary>Kredi kartı — son 4 hane korunur (sektör standardı, ör. "**** **** **** 1111").</summary>
    public static string MaskCreditCard(string? cardNumber) => MaskDigitsKeepTail(cardNumber, visibleTailDigits: 4);

    /// <summary>IPv4'te son oktet, IPv6'da ilk 2 grup dışındakiler maskelenir.</summary>
    public static string MaskIpAddress(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return ip ?? "";

        if (ip.Contains(':'))
        {
            var groups = ip.Split(':');
            var visible = Math.Min(2, groups.Length);
            return string.Join(":", groups.Take(visible)) + "::";
        }

        var octets = ip.Split('.');
        return octets.Length == 4 ? $"{octets[0]}.{octets[1]}.{octets[2]}.*" : "***";
    }

    // Telefon/TCKN/kredi kartı aynı desende: rakam olmayan karakterler (boşluk, tire, +) olduğu
    // gibi kalır — okunabilirlik için — yalnızca son N rakam görünür kalır.
    private static string MaskDigitsKeepTail(string? value, int visibleTailDigits)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";

        var totalDigits = value.Count(char.IsDigit);
        if (totalDigits == 0) return value;

        var digitsSeen = 0;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsDigit(c)) { sb.Append(c); continue; }
            digitsSeen++;
            sb.Append(digitsSeen > totalDigits - visibleTailDigits ? c : '*');
        }

        return sb.ToString();
    }
}
