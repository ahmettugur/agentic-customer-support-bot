// Döküm satırının meta alanları DB'ye yazılır ve geçmiş yeniden yüklendiğinde (yeni pod) geri gelir.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresChatBridgeVoiceTests(PostgresCatalogFixture fixture)
{
    [Fact]
    public async Task TranscriptMeta_SurvivesHydration()
    {
        var ct = TestContext.Current.CancellationToken;
        var sid = $"vb-{Guid.NewGuid():N}";
        var writer = new PostgresChatBridge(fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);
        await writer.PublishVoiceTranscriptAsync(sid, "c1", "agent", 10000, "Temsilci (Elif): Merhaba");

        var freshPod = new PostgresChatBridge(fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);
        var history = await freshPod.GetHistoryAsync(sid);
        history.Should().ContainSingle();
        history[0].VoiceCallId.Should().Be("c1");
        history[0].VoiceTrack.Should().Be("agent");
        history[0].OffsetMs.Should().Be(10000);
    }
}
