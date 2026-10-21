// Senaryo dosyasının konumu çalışma dizinine bağlı olmamalı: `dotnet run` depo kökünden, IDE ise proje dizininden
// başlatır. Eskiden yalnızca <içerik kökü>/docs ve bir üstü aranıyordu; proje dizininden başlatınca /eval/* 404 dönüyordu.

using CustomerSupportBot.Api.Infrastructure;

namespace CustomerSupportBot.Api.IntegrationTests;

public sealed class ScenarioLoaderLocateTests : IDisposable
{
    private const string FileName = "evaluation-scenarios.yaml";
    private readonly string _repo = Path.Combine(Path.GetTempPath(), "repo-" + Guid.NewGuid().ToString("N"));

    public ScenarioLoaderLocateTests()
    {
        Directory.CreateDirectory(Path.Combine(_repo, "docs"));
        File.WriteAllText(Path.Combine(_repo, "docs", FileName), "version: 1\nscenarios: []\n");
        Directory.CreateDirectory(Path.Combine(_repo, "src", "Api"));
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    [Theory]
    [InlineData("")]                 // depo kökünden başlatıldı
    [InlineData("src")]              // bir alt dizin
    [InlineData("src/Api")]          // proje dizininden başlatıldı (IDE)
    public void Locate_WalksUpFromTheStartDirectory_ToFindDocs(string relativeStart)
    {
        var start = Path.Combine(_repo, relativeStart.Replace('/', Path.DirectorySeparatorChar));

        var found = ScenarioLoader.Locate(FileName, start);

        Assert.Equal(Path.Combine(_repo, "docs", FileName), Path.GetFullPath(found!));
    }

    [Fact]
    public void Locate_ReturnsNull_WhenNoDocsFolderHasTheFile()
    {
        var empty = Path.Combine(Path.GetTempPath(), "empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            Assert.Null(ScenarioLoader.Locate(FileName, empty, includeAppBase: false));
        }
        finally { Directory.Delete(empty); }
    }
}
