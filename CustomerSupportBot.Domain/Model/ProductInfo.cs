// Core/Model/ProductInfo.cs
// Domain modeli — ürün bilgisi.
// Adapter tarafından Name ile birlikte döndürülür (record with expression destekler).

namespace CustomerSupportBot.Domain.Model;

/// <summary>
/// Ürün domain modeli.
/// </summary>
public record ProductInfo(decimal Price, int Stock, string Name = "");
