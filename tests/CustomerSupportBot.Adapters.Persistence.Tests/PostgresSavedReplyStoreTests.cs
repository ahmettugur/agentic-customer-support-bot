// Hazır yanıt deposu — gerçek Postgres: CRUD ve kısayolun veritabanında da benzersiz olması.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresSavedReplyStoreTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SavedReply Reply(string? shortcut = null) => new()
    {
        Title = "Kargo gecikmesi", Body = "Kargonuz yolda.", Shortcut = shortcut, CreatedBy = "admin"
    };

    [Fact]
    public async Task Crud_RoundTrips()
    {
        var store = new PostgresSavedReplyStore(fixture.DbFactory);
        var reply = Reply($"kargo-{Guid.NewGuid():N}"[..20]);

        await store.AddAsync(reply, Ct);
        (await store.GetAsync(reply.Id, Ct))!.Body.Should().Be("Kargonuz yolda.");
        (await store.ListAsync(Ct)).Should().Contain(r => r.Id == reply.Id);

        reply.Body = "Yarın teslim edilecek.";
        (await store.UpdateAsync(reply, Ct)).Should().BeTrue();
        (await store.GetAsync(reply.Id, Ct))!.Body.Should().Be("Yarın teslim edilecek.");

        (await store.DeleteAsync(reply.Id, Ct)).Should().BeTrue();
        (await store.GetAsync(reply.Id, Ct)).Should().BeNull();
        (await store.UpdateAsync(reply, Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Shortcut_IsUniqueInTheDatabase_ButNullsAreNot()
    {
        var store = new PostgresSavedReplyStore(fixture.DbFactory);
        var shortcut = $"iade-{Guid.NewGuid():N}"[..20];
        await store.AddAsync(Reply(shortcut), Ct);
        await store.AddAsync(Reply(null), Ct);
        await store.AddAsync(Reply(null), Ct);

        (await store.ShortcutExistsAsync(shortcut, null, Ct)).Should().BeTrue();
        Func<Task> act = () => store.AddAsync(Reply(shortcut), Ct);

        await act.Should().ThrowAsync<SavedReplyShortcutConflictException>();
    }
}
