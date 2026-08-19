// Application/Services/A2A/ConfiguredA2ASubjectAuthorizer.cs
// IA2ASubjectAuthorizer'ın yapılandırma tabanlı, VARSAYILAN OLARAK REDDEDEN implementasyonu.

using CustomerSupportBot.Application.Ports.Outbound.A2A;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Services.A2A;

/// <summary>
/// Partner → müşteri yetkisini <see cref="A2AOptions"/> üzerinden okur.
///
/// <para>
/// <b>Varsayılan davranış REDDETMEKTİR.</b> Hiçbir yapılandırma yoksa, partner tanımlı değilse
/// veya müşteri o partnerin listesinde değilse <c>false</c> döner. Bu bilinçli: A2A kanalı dış
/// sistemlere açık ve bu kapı yanlış açıldığında bedeli başka bir müşterinin sipariş geçmişidir.
/// "Yapılandırmayı unuttum" durumunun sonucu <b>sızıntı değil, çalışmama</b> olmalıdır.
/// </para>
///
/// <para>
/// Bu sınıf gerçek iş kuralının <b>yerini tutmaz</b>, yalnızca güvenli bir başlangıç noktasıdır.
/// Partner-müşteri ilişkisi bir tabloya/sözleşmeye bağlanacaksa bu port yeniden implemente
/// edilmeli; çağıran taraf hiç değişmez.
/// </para>
/// </summary>
public sealed class ConfiguredA2ASubjectAuthorizer : IA2ASubjectAuthorizer
{
    private readonly A2AOptions _options;
    private readonly ILogger<ConfiguredA2ASubjectAuthorizer> _logger;

    public ConfiguredA2ASubjectAuthorizer(
        IOptions<A2AOptions> options,
        ILogger<ConfiguredA2ASubjectAuthorizer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<bool> CanActForCustomerAsync(string partnerId, string customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(partnerId) || string.IsNullOrWhiteSpace(customerId))
            return Task.FromResult(false);

        var partner = _options.Partners.FirstOrDefault(p =>
            string.Equals(p.PartnerId, partnerId, StringComparison.Ordinal));

        if (partner is null)
        {
            _logger.LogWarning("[A2A] Tanımsız partner token değişimi denedi: {PartnerId}", partnerId);
            return Task.FromResult(false);
        }

        // "*" = bu partner tüm müşteriler adına hareket edebilir. Yalnızca gerçekten güvenilen,
        // sözleşmeli bir sistem için kullanılmalı — tek bir sızan token tüm müşterileri açar.
        var allowed = partner.AllowedCustomerIds.Contains("*")
                   || partner.AllowedCustomerIds.Contains(customerId, StringComparer.Ordinal);

        if (!allowed)
        {
            _logger.LogWarning(
                "[A2A] Partner yetkisiz müşteri için token istedi: partner={PartnerId} customer={CustomerId}",
                partnerId, customerId);
        }

        return Task.FromResult(allowed);
    }
}

/// <summary>A2A kanalının yapılandırması (<c>appsettings.json → A2A</c>).</summary>
public sealed class A2AOptions
{
    /// <summary>Kanal tamamen kapalıysa endpoint'ler hiç map edilmez.</summary>
    public bool Enabled { get; set; }

    /// <summary>Değişimle üretilen özne token'ının ömrü (dakika). Kısa tutulur — tek bir çağrı için yeterlidir.</summary>
    public int SubjectTokenMinutes { get; set; } = 5;

    /// <summary>
    /// Kartlarda ilan edilecek DIŞ adres (ör. <c>https://api.ornek.com</c>).
    ///
    /// <para>
    /// Boşsa binding URL'leri göreli kalır (<c>/a2a/order</c>). Göreli URL, kartı okuyan dış
    /// istemcinin adresi kendi başına çözmesini gerektirir; proxy/gateway arkasında bu çoğu
    /// zaman yanlış sonuç verir. Dışa açılan bir kurulumda bu değer <b>doldurulmalıdır</b>.
    /// </para>
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Partner başına dakikadaki istek sınırı. Dış kanal olduğu için varsayılan muhafazakârdır.</summary>
    public int RequestsPerMinute { get; set; } = 60;

    /// <summary>
    /// Tek bir A2A çağrısındaki toplam metin uzunluğu (karakter).
    ///
    /// <para>
    /// İstek SAYISI sınırı tek başına yetmez: dakikada 60 istek hakkı olan bir partner, her
    /// isteğe çok büyük bir metin koyarak hem token maliyetini hem de çağrı süresini serbestçe
    /// büyütebilir. Sınır burada, LLM'e gitmeden ÖNCE uygulanır — maliyet zaten oluştuktan
    /// sonra tespit etmenin faydası olmaz.
    /// </para>
    /// </summary>
    public int MaxMessageChars { get; set; } = 4000;

    /// <summary>
    /// Tek bir çağrıdaki azami mesaj/parça sayısı. Uzunluk sınırını çok sayıda küçük parçaya
    /// bölerek dolaşmayı engeller.
    /// </summary>
    public int MaxParts { get; set; } = 20;

    /// <summary>
    /// A2A uçlarında azami istek gövdesi (bayt). Kestrel'in varsayılanı 30 MB'dır ve bu, metin
    /// tabanlı bir sorgu kanalı için anlamsız derecede geniştir; gövde daha ayrıştırılmadan
    /// reddedilmesi en ucuz savunmadır.
    /// </summary>
    public long MaxRequestBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Partner entegrasyon dokümanının adresi. Kök agent card'ında <c>documentationUrl</c>
    /// olarak yayınlanır.
    ///
    /// <para>
    /// Boş bırakılırsa alan hiç yazılmaz — var olmayan bir adresi ilan etmek, hiç ilan
    /// etmemekten kötüdür. Bu alan özellikle önemli çünkü A2A'nın kök keşif yolu <b>tek</b>
    /// bir ajan tanımlar; diğer ajanların kartlarına giden tek meşru işaret budur.
    /// </para>
    /// </summary>
    public string DocumentationUrl { get; set; } = "";

    public List<A2APartnerOptions> Partners { get; set; } = new();
}

public sealed class A2APartnerOptions
{
    public string PartnerId { get; set; } = "";

    /// <summary>Bu partnerin adına hareket edebileceği müşteri kimlikleri. <c>"*"</c> = tümü.</summary>
    public List<string> AllowedCustomerIds { get; set; } = new();
}
