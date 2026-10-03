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
using Microsoft.Extensions.DependencyInjection;

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
    public void VoiceInstructionsPrompt_IsDeployedWithTheApi()
    {
        var prompts = factory.Services.GetRequiredService<IPromptRepository>();

        prompts.Get("services/realtime-voice").Should().Contain("pendingApproval");
    }
}
