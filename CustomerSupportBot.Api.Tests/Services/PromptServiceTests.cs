using CustomerSupportBot.Api.Services;
using CustomerSupportBot.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Tests.Services;

public class PromptServiceTests
{
    private readonly PromptService _svc = new(NullLogger<PromptService>.Instance);

    [Fact]
    public void Keys_ContainsKnownAgents()
    {
        _svc.Keys.Should().NotBeEmpty();
    }

    [Fact]
    public void Get_UnknownKey_Throws()
    {
        Action act = () => _svc.Get("nope/missing");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Get_KnownKey_ReturnsContent()
    {
        var anyKey = _svc.Keys.First();
        _svc.Get(anyKey).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Render_NullVariables_StripsPlaceholders()
    {
        // Bilinen prompt: services/reasoning-system — placeholder içerir
        var anyKey = _svc.Keys.First();
        var rendered = _svc.Render(anyKey, null);
        rendered.Should().NotContain("{{");
    }

    [Fact]
    public void Render_WithVariables_SubstitutesPlaceholders()
    {
        var anyKey = _svc.Keys.First();
        var raw = _svc.Get(anyKey);
        // Eğer template'te en az bir {{KEY}} varsa, onu boş olmayan değerle değiştir
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}");
        if (!match.Success)
            return; // pas geç
        var key = match.Groups[1].Value;
        var rendered = _svc.Render(anyKey, new Dictionary<string, string?> { [key] = "MARKED" });
        rendered.Should().Contain("MARKED");
        rendered.Should().NotContain($"{{{{{key}}}}}");
    }
}
