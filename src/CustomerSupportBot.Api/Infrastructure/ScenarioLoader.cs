// Infrastructure/ScenarioLoader.cs
// YAML dosyasından evaluation senaryolarını yükler.
// YamlDotNet bağımlılığı altyapı katmanında (API) izole edilir.

using CustomerSupportBot.Application.Ports.Inbound;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CustomerSupportBot.Api.Infrastructure;

public static class ScenarioLoader
{
    /// <summary>YAML dosyasından senaryoları yükler.</summary>
    public static ScenarioFile LoadScenarios(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        return deserializer.Deserialize<ScenarioFile>(yaml);
    }
}
