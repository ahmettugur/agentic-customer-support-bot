// Veri saklama süresi taramasını tetikleyen arka plan işi: çok pod'lu kurulumda tek pod çalıştırır.

using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Api.Workers;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Api.IntegrationTests;

public class DataRetentionServiceTests
{
    private static (DataRetentionService Worker, IDataPrivacyPort Port, InMemoryDistributedLock Lock) Build()
    {
        var port = Substitute.For<IDataPrivacyPort>();
        port.RunRetentionAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new RetentionResult(true, 0, 0, []));
        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 }));
        var options = Substitute.For<IOptionsMonitor<DataRetentionOptions>>();
        options.CurrentValue.Returns(new DataRetentionOptions());
        var worker = new DataRetentionService(port, options, NullLogger<DataRetentionService>.Instance, locks);
        return (worker, port, locks);
    }

    [Fact]
    public async Task RunOnce_RunsTheSweep()
    {
        var (worker, port, _) = Build();

        await worker.RunOnceAsync(CancellationToken.None);

        await port.Received(1).RunRetentionAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunOnce_WhenAnotherPodHoldsTheLock_Skips()
    {
        var (worker, port, locks) = Build();
        await using var held = await locks.TryAcquireAsync(DataRetentionService.LockKey, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        await worker.RunOnceAsync(CancellationToken.None);

        await port.DidNotReceive().RunRetentionAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}
