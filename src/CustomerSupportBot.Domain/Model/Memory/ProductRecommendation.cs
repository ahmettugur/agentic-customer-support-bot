// Models/Memory/ProductRecommendation.cs
// Recommendation aşamasının çıktısı — Understanding'den türetilir, Action'ı TETİKLEMEZ.

namespace CustomerSupportBot.Domain.Model.Memory;

/// <summary>
/// Tek bir ürün önerisi — <b>öneri</b>, talimat değil. Agent bunu kullanıcıya iletip
/// iletmemekte serbesttir; hiçbir tool'u otomatik tetiklemez.
/// </summary>
/// <param name="ProductName">Katalogdaki kanonik ürün adı.</param>
/// <param name="Reason">Kısa, insan-okur gerekçe (ör. "İçecekler kategorisinde ilgilendiği ürüne yakın").</param>
/// <param name="Category">Ürünün kategorisi.</param>
public sealed record ProductRecommendation(string ProductName, string Reason, string Category);
