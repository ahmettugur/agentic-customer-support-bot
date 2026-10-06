// Infrastructure/RateLimitingOptions.cs
// appsettings "RateLimiting": "general" hız sınırı politikasının kotaları.

namespace CustomerSupportBot.Api.Infrastructure;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Müşteri (müşteri başına) ve kimliksiz istek (IP başına) için dakikalık izin.</summary>
    public int GeneralPerMinute { get; set; } = 60;

    /// <summary>
    /// Yönetici/temsilci (kullanıcı başına) dakikalık izin. Panel 15 sn'de bir yoklar, açık canlı sohbette 5 sn'de
    /// bir duygu durumu, SLA sayfası 5 sn'de bir iki istek atar; birkaç sekme ve sekme geçişleriyle bir kullanıcı
    /// dakikada 60'ı rahatça aşar.
    /// </summary>
    public int StaffPerMinute { get; set; } = 300;
}
