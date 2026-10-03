using CustomerSupportBot.Adapters.Persistence.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class PromptServiceTests
{
    private readonly FileSystemPromptRepository _svc = new(NullLogger<FileSystemPromptRepository>.Instance);

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

    /// <summary>
    /// Sesli asistanın talimatı da bu klasörden gelir (eskiden koda gömülüydü). Yazılı ajan
    /// prompt'larındaki kritik kurallar orada da bulunmalı — özellikle kaydın kime ait olduğunu
    /// sızdırmama ve "onaya gönderildi"yi "tamamlandı" diye sunmama.
    /// </summary>
    [Theory]
    [InlineData("CUSTOMER_ID_MISMATCH")]
    [InlineData("COMPLAINT_NOT_FOUND")]
    [InlineData("pendingApproval")]
    [InlineData("STOCK_INSUFFICIENT")]
    [InlineData("human_handoff_tool")]
    [InlineData("end_conversation")]
    public void RealtimeVoicePrompt_CarriesTheRulesItMustEnforce(string rule)
    {
        _svc.Get("services/realtime-voice").Should().Contain(rule);
    }
}
