// Sesli görüşmenin bağımlılık grafiği gerçek API kapsayıcısında çözülüyor mu?
//
// DI doğrulaması (ValidateOnBuild) açık değil; sesli servis yalnızca bir WebSocket bağlantısı
// geldiğinde çözüldüğü için eksik bir kayıt (ör. onay kapısı, prompt deposu) ancak ilk sesli
// görüşmede patlardı. Onay kapısının opsiyonel parametre olması özellikle tehlikeli: kayıt
// eksik olsa servis yine kurulur ama yan etkili her sesli işlem "APPROVAL_UNAVAILABLE" döner.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Application.Services.Realtime;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.IntegrationTests;

public class RealtimeVoiceCompositionTests(TestWebApplicationFactory factory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task VoiceServiceGraph_Resolves()
    {
        // Adaptör yalnızca IAsyncDisposable uygular — kapsam asenkron kapatılmalı.
        await using var scope = factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IRealtimeNativeBridge>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IRealtimeVoiceTransport>().Should().NotBeNull();
        scope.ServiceProvider.GetService<SideEffectApprovalGate>().Should().NotBeNull(
            "kayıt eksikse sesli kanalda sipariş/iptal/iade/şikayet sessizce devre dışı kalır");
    }

    [Fact]
    public void WithoutASetting_TheEngineIsRealtime()
    {
        new VoiceEngineOptions().Engine.Should().Be(VoiceEngine.Realtime);
    }

    [Fact]
    public async Task RealtimeEngine_IsChosenFromTheSettings()
    {
        // Geliştiricinin appsettings dosyasındaki seçimden bağımsız: motor burada açıkça verilir.
        await using var realtime = factory.WithWebHostBuilder(b => b.UseSetting("AI:Realtime:Engine", "Realtime"));
        await using var scope = realtime.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IRealtimeNativeBridge>().Should().BeOfType<RealtimeNativeService>();
    }

    [Fact]
    public async Task LiveEngine_IsChosenFromTheSettings_AndItsGraphResolves()
    {
        await using var live = factory.WithWebHostBuilder(b => b.UseSetting("AI:Realtime:Engine", "Live"));
        await using var scope = live.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IRealtimeNativeBridge>().Should().BeOfType<LiveVoiceService>();
        scope.ServiceProvider.GetRequiredService<ILiveVoiceTransport>().ModelName.Should().Be("gpt-live-1");
    }

    [Fact]
    public async Task EngineChange_AppliesToTheNextCall_WithoutARestart()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var monitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitorCache<VoiceEngineOptions>>();
        monitor.TryRemove(Options.DefaultName);
        monitor.TryAdd(Options.DefaultName, new VoiceEngineOptions { Engine = VoiceEngine.Live });
        try
        {
            await using var next = factory.Services.CreateAsyncScope();
            next.ServiceProvider.GetRequiredService<IRealtimeNativeBridge>().Should().BeOfType<LiveVoiceService>();
        }
        finally
        {
            monitor.TryRemove(Options.DefaultName);
        }
    }

    [Fact]
    public void InvalidEngine_FailsAtStartup()
    {
        using var bad = factory.WithWebHostBuilder(b => b.UseSetting("AI:Realtime:Engine", "Canli"));

        var start = () => bad.Services;

        start.Should().Throw<Exception>();
    }

    [Fact]
    public void LivePrompts_AreDeployedWithTheApi()
    {
        var prompts = factory.Services.GetRequiredService<IPromptRepository>();

        prompts.Get("services/live-voice").Should().Contain("<|SpawnThinking|>");
        prompts.Get("services/live-backend").Should().Contain("pendingApproval");
    }

    [Fact]
    public void VoiceInstructionsPrompt_IsDeployedWithTheApi()
    {
        var prompts = factory.Services.GetRequiredService<IPromptRepository>();

        prompts.Get("services/realtime-voice").Should().Contain("pendingApproval");
    }
}
