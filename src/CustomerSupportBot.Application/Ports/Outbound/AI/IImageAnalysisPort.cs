namespace CustomerSupportBot.Application.Ports.Outbound.AI;

/// <summary>
/// Görsel anlayan model — müşteri fotoğrafını ajanların kullanacağı kısa bir Türkçe açıklamaya çevirir
/// (ürün, hasar, görünen ürün/sipariş etiketi). Kişisel verileri yazıya dökmemesi talimatla istenir.
/// </summary>
public interface IImageAnalysisPort
{
    Task<string> DescribeAsync(byte[] image, string contentType, CancellationToken ct = default);
}
