// Services/Telemetry/ICostCalculator.cs
// Token kullanımını USD tahmini maliyete çevirir. Gerçek faturalama için değil,
// trend ve uyarı için kullanılır. Fiyat tablosu appsettings'ten yüklenir.

namespace CustomerSupportBot.Services.Telemetry;

public interface ICostCalculator
{
    /// <summary>
    /// Verilen model için input/output token'ı USD'ye çevirir.
    /// Model bilinmiyorsa "default" girişi kullanılır; o da yoksa 0 döner.
    /// </summary>
    decimal Estimate(string? model, long inputTokens, long outputTokens);

    /// <summary>Bilinen model adlarını döner (UI için).</summary>
    IReadOnlyCollection<string> KnownModels { get; }
}
