// Application/Ports/Driven/IPromptRepository.cs
// SECONDARY PORT — Prompt şablon erişimi.
// Implementasyon (FileSystemPromptRepository) Adapters.Persistence katmanında Prompts/*.md dosyalarını yükler.

namespace CustomerSupportBot.Application.Ports.Driven;

/// <summary>
/// Prompt şablonlarına erişim için secondary port.
/// Implementasyon Adapters.Persistence katmanında; Application katmanı bu arayüze bağımlıdır.
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
