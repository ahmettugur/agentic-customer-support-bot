namespace CustomerSupportBot.Application.Ports.Outbound.Observability;

/// <summary>
/// LLM kullanım maliyeti hesaplama için secondary port.
/// TelemetryChatClient bu port üzerinden token başına maliyet hesaplar.
/// </summary>
public interface ICostCalculatorPort
{
    /// <summary>
    /// Verilen model + token sayısı için USD maliyeti hesaplar.
    /// </summary>
    decimal CalculateCost(string modelHint, string provider, int inputTokens, int outputTokens);

    /// <summary>Bilinen model adlarını döner (UI için).</summary>
    IReadOnlyCollection<string> KnownModels { get; }
}
