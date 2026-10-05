// Konuşma kapanış kayıtları — gerçek Postgres (text[] etiketler, unnest ile sayım, KVKK silmesi).

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresConversationDispositionStoreTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private PostgresConversationDispositionStore Store() => new(fixture.DbFactory);

    private static ConversationDisposition D(string sessionId, string reason, params string[] tags) => new()
    {
        SessionId = sessionId, ReasonCode = reason, Tags = [.. tags], Note = "not", ClosedBy = "jane",
        ClosedAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Add_ThenListForSession()
    {
        var store = Store();
        var sid = Unique("s");
        await store.AddAsync(D(sid, "resolved", "kargo", "iade"), Ct);
        await store.AddAsync(D(Unique("s"), "resolved"), Ct);

        var saved = (await store.ListForSessionAsync(sid, Ct)).Should().ContainSingle().Subject;
        saved.Tags.Should().Equal("kargo", "iade");
        saved.Note.Should().Be("not");
        saved.ClosedBy.Should().Be("jane");
        saved.ClosedAt.Should().Be(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task CountsReasons_AndTags()
    {
        var store = Store();
        var (r1, r2) = (Unique("r"), Unique("r"));
        var (t1, t2) = (Unique("t"), Unique("t"));
        await store.AddAsync(D(Unique("s"), r1, t1, t2), Ct);
        await store.AddAsync(D(Unique("s"), r1, t1), Ct);
        await store.AddAsync(D(Unique("s"), r2, t1), Ct);

        var reasons = await store.CountByReasonAsync(Ct);
        reasons[r1].Should().Be(2);
        reasons[r2].Should().Be(1);

        var tags = await store.TopTagsAsync(1000, Ct);
        tags.Should().ContainInOrder(new TagUsage(t1, 3), new TagUsage(t2, 1));
    }

    [Fact]
    public async Task EraseSessions_RemovesOnlyThoseSessions()
    {
        var store = Store();
        var (erase, keep) = (Unique("s"), Unique("s"));
        await store.AddAsync(D(erase, "resolved"), Ct);
        await store.AddAsync(D(erase, "other"), Ct);
        await store.AddAsync(D(keep, "resolved"), Ct);

        (await store.EraseSessionsAsync([erase], Ct)).Should().Be(2);

        (await store.ListForSessionAsync(erase, Ct)).Should().BeEmpty();
        (await store.ListForSessionAsync(keep, Ct)).Should().ContainSingle();
    }
}
