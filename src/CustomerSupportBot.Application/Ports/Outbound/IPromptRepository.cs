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

    /// <summary>Bir prompt'un içerik sürümü (8 hex). Anahtar yoksa <see cref="KeyNotFoundException"/>.</summary>
    string VersionOf(string key);

    /// <summary>Anahtar → içerik sürümü.</summary>
    IReadOnlyDictionary<string, string> Versions { get; }

    /// <summary>
    /// Tüm prompt'ların birleşik sürümü. Trace'e ve Langfuse'a damgalanır: "bu yanıt hangi prompt setiyle
    /// üretildi?" sorusunun cevabı ve sürümler arası karşılaştırmanın anahtarı.
    /// </summary>
    string SetVersion { get; }
}
