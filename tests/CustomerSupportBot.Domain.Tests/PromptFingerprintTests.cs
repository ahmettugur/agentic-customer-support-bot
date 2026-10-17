// Prompt içerik sürümü: hangi prompt metninin hangi yanıtı ürettiğini trace'ten okuyabilmek için.
// Sürüm, içerikten türetilir (elle sürüm numarası unutulur); satır sonu farkı sürümü değiştirmemeli.

using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Domain.Tests;

public class PromptFingerprintTests
{
    [Fact]
    public void Of_IsStableForTheSameContent()
    {
        Assert.Equal(PromptFingerprint.Of("Sen bir destek ajanısın."), PromptFingerprint.Of("Sen bir destek ajanısın."));
    }

    [Fact]
    public void Of_ChangesWhenTheContentChanges()
    {
        Assert.NotEqual(PromptFingerprint.Of("Sen bir destek ajanısın."), PromptFingerprint.Of("Sen bir satış ajanısın."));
    }

    [Fact]
    public void Of_IgnoresLineEndingDifferences_SoWindowsAndLinuxCheckoutsAgree()
    {
        Assert.Equal(PromptFingerprint.Of("satır 1\nsatır 2\n"), PromptFingerprint.Of("satır 1\r\nsatır 2\r\n"));
    }

    [Fact]
    public void Of_IsShortLowercaseHex()
    {
        var v = PromptFingerprint.Of("x");

        Assert.Equal(8, v.Length);
        Assert.Matches("^[0-9a-f]{8}$", v);
    }

    [Fact]
    public void SetVersion_DoesNotDependOnEnumerationOrder()
    {
        var a = new Dictionary<string, string> { ["agents/order"] = "11111111", ["agents/plan"] = "22222222" };
        var b = new Dictionary<string, string> { ["agents/plan"] = "22222222", ["agents/order"] = "11111111" };

        Assert.Equal(PromptFingerprint.SetVersion(a), PromptFingerprint.SetVersion(b));
    }

    [Fact]
    public void SetVersion_ChangesWhenAnyOnePromptChanges()
    {
        var before = new Dictionary<string, string> { ["agents/order"] = "11111111", ["agents/plan"] = "22222222" };
        var after = new Dictionary<string, string> { ["agents/order"] = "11111111", ["agents/plan"] = "33333333" };

        Assert.NotEqual(PromptFingerprint.SetVersion(before), PromptFingerprint.SetVersion(after));
    }

    [Fact]
    public void SetVersion_ChangesWhenAPromptIsRenamed()
    {
        var before = new Dictionary<string, string> { ["agents/order"] = "11111111" };
        var after = new Dictionary<string, string> { ["agents/orders"] = "11111111" };

        Assert.NotEqual(PromptFingerprint.SetVersion(before), PromptFingerprint.SetVersion(after));
    }
}
