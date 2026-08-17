namespace CustomerSupportBot.Application.Ports.Outbound;

/// <summary>
/// Prompt şablonlarına erişim için secondary port.
/// </summary>
public interface IPromptRepository
{
    /// <summary>Verilen anahtara karşılık gelen prompt'u döner.</summary>
    string Get(string key);

    /// <summary>Prompt'u yükler ve {{PLACEHOLDER}} değişkenlerini ikame eder.</summary>
    string Render(string key, IDictionary<string, string?>? variables = null);

    /// <summary>Kayıtlı tüm prompt anahtarları.</summary>
    IReadOnlyCollection<string> Keys { get; }
}
