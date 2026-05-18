// Ports/Driven/Observability/ICostCalculatorPort.cs
// SECONDARY PORT — LLM token maliyet hesaplama.
// Adaptör: CostCalculatorAdapter (CustomerSupportBot.Adapters.Telemetry)

namespace CustomerSupportBot.Application.Ports.Driven.Observability;

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
}
