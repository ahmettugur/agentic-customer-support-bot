// Kompozisyon: çökme sonrası devam servisi DI'dan çözülür ve VARSAYILAN KAPALI iken hiçbir şeye dokunmaz.

using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.IntegrationTests;

public class WorkflowRecoveryCompositionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public WorkflowRecoveryCompositionTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void CheckpointingIsOffByDefault()
    {
        _factory.Services.GetRequiredService<IOptions<WorkflowCheckpointingOptions>>().Value.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task RecoveryPort_Resolves_AndDoesNothingWhileDisabled()
    {
        var recovery = _factory.Services.GetRequiredService<IWorkflowRecoveryPort>();

        var result = await recovery.RecoverStaleTurnsAsync(TestContext.Current.CancellationToken);

        result.Should().Be(new WorkflowRecoveryResult(0, 0, 0, 0));
    }

    [Fact]
    public void ShutdownSignal_IsRegistered_AndNotStoppingWhileTheHostRuns()
    {
        _factory.Services.GetRequiredService<IShutdownSignal>().IsStopping.Should().BeFalse();
    }

    [Fact]
    public void RecoveryWorker_DoesNotResolveTheChatClientChain_AtConstruction()
    {
        // Dolaylı kanıt: işçi yalnızca IServiceProvider ister. Doğrudan IWorkflowRecoveryPort isteseydi host açılışında
        // CustomerSupportTeam -> IChatClient çözülür ve anahtarsız ortamda açılış düşerdi (CorsPolicyTests bunu yakaladı).
        var ctor = typeof(CustomerSupportBot.Api.Workers.WorkflowRecoveryWorker).GetConstructors().Single();

        ctor.GetParameters().Select(p => p.ParameterType).Should().NotContain(typeof(IWorkflowRecoveryPort));
    }
}
