// Tests/Services/PostgresEscalationAgentScopeTests.cs
//
// Agent kapsamlı eskalasyon geçmişinin GERÇEK Postgres'e karşı doğrulanması.
//
// Bu testin in-memory karşılığı (EscalationAgentScopeTests) bu hatayı yakalayamaz ve bu
// yapısaldır: InMemoryEscalationSink'te hydration sınırı YOKTUR, dolayısıyla o adaptörde
// "cache penceresinin gerisinde kalmak" diye bir durum oluşamaz. Kısıtın var olmadığı bir
// ortamda, kısıttan doğan bir hata aranamaz.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresEscalationAgentScopeTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresEscalationAgentScopeTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    /// <summary>PostgresEscalationSink.HydrateRecentCount ile aynı — cache'in kapalı-kayıt penceresi.</summary>
    private const int HydrateRecentCount = 500;

    private PostgresEscalationSink NewSink() => new(
        _fixture.DbFactory,
        new InMemoryMessageBusHub().CreateNode(),
        NullLogger<PostgresEscalationSink>.Instance);

    private static EscalationEntity Closed(string assignedTo, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid().ToString("N")[..12],
        SessionId = $"s-{Guid.NewGuid():N}",
        UserQuery = "test",
        Reason = "test",
        MissingContextJson = "[]",
        CreatedAt = createdAt,
        ResolvedAt = createdAt,
        Status = "Resolved",
        AssignedTo = assignedTo
    };

    [Fact]
    public async Task GetRecentForAgent_FindsOwnRecord_BeyondTheHydrationWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var mineId = Guid.NewGuid().ToString("N")[..12];
        var agentId = $"agent-{Guid.NewGuid():N}"[..16];
        var anchor = DateTime.UtcNow;

        await using (var ctx = await _fixture.DbFactory.CreateDbContextAsync(ct))
        {
            // Çağıranın kaydı EN ESKİ olsun.
            var mine = Closed(agentId, anchor.AddDays(-30));
            mine.Id = mineId;
            ctx.Escalations.Add(mine);

            // Üstüne hydration penceresini AŞACAK kadar başkasına ait kapalı kayıt.
            ctx.Escalations.AddRange(Enumerable.Range(0, HydrateRecentCount + 50)
                .Select(i => Closed("someone-else", anchor.AddMinutes(-i))));

            await ctx.SaveChangesAsync(ct);
        }

        var sink = NewSink();

        // Önce sorunun gerçekten var olduğunu sabitle: cache tabanlı okuma bu kaydı GÖREMEZ.
        sink.GetRecent(int.MaxValue).Should().NotContain(e => e.Id == mineId,
            "cache açık kayıtlar + son 500 kapalı kayıtla sınırlıdır; bu kayıt o pencerenin dışında");

        var visible = await sink.GetRecentForAgentAsync(agentId, count: 50, ct);

        visible.Should().Contain(e => e.Id == mineId,
            "daraltma ve limit veritabanında uygulanmalı — agent kendi kaydını cache penceresinin " +
            "gerisinde kalsa bile görmeli");
        visible.Should().NotContain(e => e.AssignedTo == "someone-else",
            "başka bir agent'a atanmış kayıtlar görünmemeli");
    }

    [Fact]
    public async Task GetRecentForAgent_IncludesUnassigned_AndRespectsLimitAfterScoping()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = $"agent-{Guid.NewGuid():N}"[..16];
        var anchor = DateTime.UtcNow;
        var unassignedId = Guid.NewGuid().ToString("N")[..12];

        await using (var ctx = await _fixture.DbFactory.CreateDbContextAsync(ct))
        {
            var free = Closed(assignedTo: null!, anchor);
            free.AssignedTo = null;
            free.Id = unassignedId;
            ctx.Escalations.Add(free);

            ctx.Escalations.AddRange(Enumerable.Range(0, 10)
                .Select(i => Closed(agentId, anchor.AddMinutes(-i - 1))));

            await ctx.SaveChangesAsync(ct);
        }

        var sink = NewSink();

        var visible = await sink.GetRecentForAgentAsync(agentId, count: 5, ct);

        visible.Should().HaveCount(5, "limit daraltmadan SONRA uygulanmalı");
        visible.Should().Contain(e => e.Id == unassignedId, "atanmamış kayıtlar herkese açıktır");
    }
}
