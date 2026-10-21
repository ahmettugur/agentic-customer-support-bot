// Infrastructure/ScenarioLoader.cs
// YAML dosyasından evaluation senaryolarını yükler.
// YamlDotNet bağımlılığı altyapı katmanında (API) izole edilir.

using CustomerSupportBot.Application.Ports.Inbound;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CustomerSupportBot.Api.Infrastructure;

public static class ScenarioLoader
{
    private const int MaxLevelsUp = 8;

    /// <summary>
    /// <c>docs/&lt;fileName&gt;</c> dosyasını bulur: verilen dizinden ve (varsayılan) uygulamanın bin dizininden başlayıp
    /// her üst dizinde <c>docs</c> altına bakar. Çalışma dizininden bağımsızdır — <c>dotnet run</c> depo kökünden,
    /// IDE proje dizininden, yayımlanmış uygulama bin dizininden başlar.
    /// </summary>
    public static string? Locate(string fileName, string startDirectory, bool includeAppBase = true)
    {
        var starts = includeAppBase ? new[] { startDirectory, AppContext.BaseDirectory } : [startDirectory];
        foreach (var start in starts)
        {
            var dir = new DirectoryInfo(start);
            for (var level = 0; dir is not null && level <= MaxLevelsUp; level++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "docs", fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

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
