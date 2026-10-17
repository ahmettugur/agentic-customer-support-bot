// Trace'e damgalanan prompt seti sürümü DB'ye yazılıp geri okunuyor mu (yeni sütun: prompt_version).

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresReasoningTracePromptVersionTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresReasoningTracePromptVersionTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private PostgresReasoningTraceStore Store() =>
        new(_fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresReasoningTraceStore>.Instance);

    [Fact]
    public async Task PromptVersion_SurvivesCompleteAndReload()
    {
        var store = Store();
        var trace = await store.StartTraceAsync("oturum", "siparişim nerede");
        trace.PromptVersion = "a1b2c3d4";
        store.Update(trace);

        await store.CompleteAsync(trace.TraceId, terminationReason: "completed", finalResponse: "kargoda");

        Store().Get(trace.TraceId)!.PromptVersion.Should().Be("a1b2c3d4");
    }

    [Fact]
    public async Task TraceWithoutPromptVersion_LoadsWithNull()
    {
        var store = Store();
        var trace = await store.StartTraceAsync("oturum", "merhaba");
        await store.CompleteAsync(trace.TraceId, terminationReason: "completed");

        Store().Get(trace.TraceId)!.PromptVersion.Should().BeNull();
    }
}
