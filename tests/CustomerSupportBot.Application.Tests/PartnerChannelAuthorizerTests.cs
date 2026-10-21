// Partner → kanal yetkisi (a2a / mcp). Varsayılan: yalnızca a2a — eskiden var olan partnerler MCP'ye KENDİLİĞİNDEN
// girmemeli; yeni kanal açıkça verilmeli. Bilinmeyen partner hiçbir kanala giremez.

using System.Security.Claims;
using CustomerSupportBot.Application.Services.A2A;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class PartnerChannelAuthorizerTests
{
    private sealed class MutableMonitor(A2AOptions initial) : IOptionsMonitor<A2AOptions>
    {
        public A2AOptions CurrentValue { get; set; } = initial;
        public A2AOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<A2AOptions, string?> listener) => null;
    }

    private static MutableMonitor Monitor(params A2APartnerOptions[] partners) => new(new A2AOptions { Partners = [.. partners] });

    [Fact]
    public void Partner_WithoutExplicitChannels_GetsOnlyA2A()
    {
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(new A2APartnerOptions { PartnerId = "p" }));

        Assert.True(sut.CanUse("p", PartnerChannels.A2A));
        Assert.False(sut.CanUse("p", PartnerChannels.Mcp));
    }

    [Fact]
    public void ExplicitChannels_AreExactlyWhatIsAllowed()
    {
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(
            new A2APartnerOptions { PartnerId = "mcp-only", Channels = [PartnerChannels.Mcp] },
            new A2APartnerOptions { PartnerId = "both", Channels = [PartnerChannels.A2A, PartnerChannels.Mcp] }));

        Assert.False(sut.CanUse("mcp-only", PartnerChannels.A2A));
        Assert.True(sut.CanUse("mcp-only", PartnerChannels.Mcp));
        Assert.True(sut.CanUse("both", PartnerChannels.A2A));
        Assert.True(sut.CanUse("both", PartnerChannels.Mcp));
    }

    [Fact]
    public void ChannelNames_AreCaseInsensitive()
    {
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(new A2APartnerOptions { PartnerId = "p", Channels = ["MCP"] }));

        Assert.True(sut.CanUse("p", "mcp"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingPartnerIdentity_IsDeniedEverywhere(string? partnerId)
    {
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(new A2APartnerOptions { PartnerId = "p", Channels = ["a2a", "mcp"] }));

        Assert.False(sut.CanUse(partnerId, PartnerChannels.A2A));
        Assert.False(sut.CanUse(partnerId, PartnerChannels.Mcp));
        Assert.False(sut.CanUseAny(partnerId));
    }

    [Fact]
    public void UnconfiguredPartner_KeepsLegacyA2AAccess_ButNeverGetsMcp()
    {
        // Geriye dönük uyum: A2A:Partners'ta olmayan Partner-rollü hesap katalog ajanını eskiden kullanabiliyordu.
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(new A2APartnerOptions { PartnerId = "p" }));

        Assert.True(sut.CanUse("tanımsız", PartnerChannels.A2A));
        Assert.False(sut.CanUse("tanımsız", PartnerChannels.Mcp), "yeni kanal açıkça verilmeden açılmaz");
        Assert.False(sut.CanUseAny("tanımsız"), "token değişimi için partnerin tanımlı olması gerekir");
    }

    [Fact]
    public void ConfiguredPartner_ExplicitlyWithoutA2A_IsRefusedOnA2A()
    {
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(new A2APartnerOptions { PartnerId = "mcp-only", Channels = ["mcp"] }));

        Assert.False(sut.CanUse("mcp-only", PartnerChannels.A2A));
    }

    [Fact]
    public void PartnerIdMatching_IsExact()
    {
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(new A2APartnerOptions { PartnerId = "Acme", Channels = ["mcp"] }));

        Assert.False(sut.CanUse("acme", PartnerChannels.Mcp));   // farklı harf = tanımsız partner → mcp yok
    }

    [Fact]
    public void EmptyChannelList_DisablesAConfiguredPartnerEntirely()
    {
        var sut = new ConfiguredPartnerChannelAuthorizer(Monitor(new A2APartnerOptions { PartnerId = "p", Channels = [] }));

        Assert.False(sut.CanUse("p", PartnerChannels.A2A));
        Assert.False(sut.CanUseAny("p"));
    }

    [Fact]
    public void ConfigChanges_TakeEffectImmediately_SoAccessCanBeRevokedWithoutARestart()
    {
        var monitor = Monitor(new A2APartnerOptions { PartnerId = "p", Channels = ["a2a", "mcp"] });
        var sut = new ConfiguredPartnerChannelAuthorizer(monitor);
        Assert.True(sut.CanUse("p", PartnerChannels.Mcp));

        monitor.CurrentValue = new A2AOptions { Partners = [new A2APartnerOptions { PartnerId = "p", Channels = ["a2a"] }] };

        Assert.False(sut.CanUse("p", PartnerChannels.Mcp));
    }

    // ═══ partner kimliğinin token'dan çözülmesi ═══

    private static ClaimsPrincipal Principal(string? nameIdentifier, string? name) =>
        new(new ClaimsIdentity(
            new[] { nameIdentifier is null ? null : new Claim(ClaimTypes.NameIdentifier, nameIdentifier), name is null ? null : new Claim(ClaimTypes.Name, name) }
                .Where(c => c is not null)!, "test"));

    [Fact]
    public void PartnerToken_ResolvesFromTheUserName_NotTheGuid()
    {
        Assert.Equal("acme", A2APartnerIdentity.Resolve(Principal(Guid.NewGuid().ToString("N"), "acme")));
    }

    [Fact]
    public void SubjectToken_ResolvesTheEmbeddedPartner()
    {
        Assert.Equal("acme", A2APartnerIdentity.Resolve(Principal(A2ASubjectIdentity.BuildId("acme", "1027"), "a2a:acme")));
    }

    [Fact]
    public void NoIdentity_ResolvesToNull() => Assert.Null(A2APartnerIdentity.Resolve(Principal(null, null)));
}
