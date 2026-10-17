// FileSystemPromptRepository'nin prompt sürümleri: dosya içeriğinden türer, düzenleme sürümü değiştirir.

using CustomerSupportBot.Adapters.Persistence.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public sealed class FileSystemPromptVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "prompts-" + Guid.NewGuid().ToString("N"));

    public FileSystemPromptVersionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "agents"));
        File.WriteAllText(Path.Combine(_root, "agents", "order.md"), "Sipariş ajanı v1");
        File.WriteAllText(Path.Combine(_root, "agents", "plan.md"), "Planlama ajanı v1");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private FileSystemPromptRepository Build() =>
        new(NullLogger<FileSystemPromptRepository>.Instance, Options.Create(new PromptOptions { RootPath = _root }));

    [Fact]
    public void Versions_HasAnEntryPerPrompt_KeyedLikeGet()
    {
        var repo = Build();

        Assert.Equal(["agents/order", "agents/plan"], repo.Versions.Keys.Order());
        Assert.Equal(repo.Versions["agents/order"], repo.VersionOf("agents/order"));
    }

    [Fact]
    public void VersionOf_UnknownKey_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Build().VersionOf("agents/yok"));
    }

    [Fact]
    public void EditingAPrompt_ChangesItsVersionAndTheSetVersion_ButNotOthers()
    {
        var before = Build();
        File.WriteAllText(Path.Combine(_root, "agents", "order.md"), "Sipariş ajanı v2");
        var after = Build();

        Assert.NotEqual(before.VersionOf("agents/order"), after.VersionOf("agents/order"));
        Assert.Equal(before.VersionOf("agents/plan"), after.VersionOf("agents/plan"));
        Assert.NotEqual(before.SetVersion, after.SetVersion);
    }

    [Fact]
    public void SameFiles_GiveTheSameSetVersion_AcrossInstances()
    {
        Assert.Equal(Build().SetVersion, Build().SetVersion);
    }
}
