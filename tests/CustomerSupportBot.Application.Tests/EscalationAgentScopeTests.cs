// Agent'ın görebileceği eskalasyon geçmişinin kapsamı — port seviyesinde sözleşme testi.
//
// DİKKAT: Bu testler InMemoryEscalationSink kullanır ve o adaptörde hydration sınırı YOKTUR.
// Dolayısıyla "kayıt cache penceresinin gerisinde kaldı" hatasını yakalayamazlar; kısıtın
// var olmadığı bir ortamda kısıttan doğan hata aranamaz. O senaryonun asıl kanıtı gerçek
// Postgres'e karşı yazılmıştır: PostgresEscalationAgentScopeTests.
//
// Buradaki testler yalnızca sözleşmeyi sabitler: atanmamış + kendine atanmış kayıtlar görünür,
// başkasınınkiler görünmez, limit daraltmadan SONRA uygulanır.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Services.Escalation;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Application.Tests;

public class EscalationAgentScopeTests
{
    private static EscalationPortService NewService(out InMemoryEscalationSink sink)
    {
        sink = new InMemoryEscalationSink(NullLogger<InMemoryEscalationSink>.Instance);
        return new EscalationPortService(sink, NullLogger<EscalationPortService>.Instance);
    }

    [Fact]
    public async Task GetRecentForAgent_ScopesToUnassignedAndOwnRecords()
    {
        var service = NewService(out var sink);

        // Çağıranın kaydı EN ESKİ olsun — sonra 60 tane başkasına ait kayıt gelsin.
        var mine = sink.Create(new EscalationRequest
        {
            SessionId = "s-mine", Reason = "benim kaydım", AssignedTo = "agent-1"
        });

        for (var i = 0; i < 60; i++)
            sink.Create(new EscalationRequest
            {
                SessionId = $"s-other-{i}", Reason = "başkasının", AssignedTo = "agent-2"
            });

        var visible = await service.GetRecentForAgentAsync("agent-1", count: 50, TestContext.Current.CancellationToken);

        visible.Should().Contain(e => e.Id == mine.Id,
            "daraltma limitten önce uygulanmalı; aksi hâlde son 50 kaydın tamamı başkasına " +
            "ait olduğu için çağıran kendi kaydını hiç göremez");
        visible.Should().NotContain(e => e.AssignedTo == "agent-2",
            "başka bir agent'a atanmış kayıtlar görünmemeli");
    }

    [Fact]
    public async Task GetRecentForAgent_IncludesUnassignedRecords()
    {
        var service = NewService(out var sink);

        var unassigned = sink.Create(new EscalationRequest { SessionId = "s-free", Reason = "atanmamış" });
        var others = sink.Create(new EscalationRequest
        {
            SessionId = "s-other", Reason = "başkasının", AssignedTo = "agent-9"
        });

        var visible = await service.GetRecentForAgentAsync("agent-1", ct: TestContext.Current.CancellationToken);

        visible.Should().Contain(e => e.Id == unassigned.Id, "atanmamış kayıtlar herkese açıktır");
        visible.Should().NotContain(e => e.Id == others.Id);
    }

    [Fact]
    public async Task GetRecentForAgent_RespectsTheLimitAfterScoping()
    {
        var service = NewService(out var sink);
        for (var i = 0; i < 10; i++)
            sink.Create(new EscalationRequest { SessionId = $"s-{i}", Reason = "x", AssignedTo = "agent-1" });

        (await service.GetRecentForAgentAsync("agent-1", count: 3, TestContext.Current.CancellationToken))
            .Should().HaveCount(3);
    }
}
