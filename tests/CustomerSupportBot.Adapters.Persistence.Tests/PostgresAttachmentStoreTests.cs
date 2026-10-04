// PostgresAttachmentStore — gerçek Postgres: kaydet/getir, veri olmadan listele, onaya bağla, cascade.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresAttachmentStoreTests(PostgresCatalogFixture fixture)
{
    private PostgresAttachmentStore Store => new(fixture.DbFactory);

    private async Task<string> InsertSessionAsync()
    {
        var sessionId = $"att-{Guid.NewGuid():N}";
        await using var ctx = fixture.DbFactory.CreateDbContext();
        ctx.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId, CreatedAt = DateTime.UtcNow, LastActivity = DateTime.UtcNow, StateJson = "{}"
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return sessionId;
    }

    private static ChatAttachment Photo(string sessionId, int seconds = 0) => new()
    {
        SessionId = sessionId,
        CustomerId = "1001",
        ContentType = "image/jpeg",
        Data = [0xFF, 0xD8, 0xFF, 0x01, 0x02],
        Description = "Kırık kupa.",
        CreatedAt = DateTime.UtcNow.AddSeconds(seconds)
    };

    [Fact]
    public async Task Save_Get_And_List_WithoutData()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await InsertSessionAsync();
        var first = Photo(sessionId);
        var second = Photo(sessionId, 1);
        await Store.SaveAsync(second, ct);
        await Store.SaveAsync(first, ct);

        var loaded = await Store.GetAsync(first.Id, ct);
        loaded!.Data.Should().Equal(first.Data);
        loaded.Description.Should().Be("Kırık kupa.");
        loaded.CustomerId.Should().Be("1001");

        var list = await Store.ListForSessionAsync(sessionId, ct);
        list.Select(a => a.Id).Should().Equal(first.Id, second.Id);
        list.Should().OnlyContain(a => a.Data.Length == 0, "liste görüntü verisini taşımamalı");
    }

    [Fact]
    public async Task Link_OnlyLinksUnlinkedPhotos()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await InsertSessionAsync();
        var a = Photo(sessionId);
        var b = Photo(sessionId, 1);
        await Store.SaveAsync(a, ct);
        await Store.SaveAsync(b, ct);

        await Store.LinkToApprovalAsync([a.Id], "appr-1", ct);
        await Store.LinkToApprovalAsync([a.Id, b.Id], "appr-2", ct);

        (await Store.GetAsync(a.Id, ct))!.ApprovalId.Should().Be("appr-1");
        (await Store.GetAsync(b.Id, ct))!.ApprovalId.Should().Be("appr-2");
    }

    [Fact]
    public async Task DeletingTheSession_DeletesItsPhotos()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await InsertSessionAsync();
        var photo = Photo(sessionId);
        await Store.SaveAsync(photo, ct);

        await using (var ctx = fixture.DbFactory.CreateDbContext())
            await ctx.Sessions.Where(s => s.SessionId == sessionId).ExecuteDeleteAsync(ct);

        (await Store.GetAsync(photo.Id, ct)).Should().BeNull();
    }

    [Fact]
    public async Task MarkSent_KeepsTheFirstTime_AndDeleteUnsent_SparesSentPhotos()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = await InsertSessionAsync();
        var sent = Photo(sessionId);
        var unsent = Photo(sessionId, 1);
        await Store.SaveAsync(sent, ct);
        await Store.SaveAsync(unsent, ct);
        var first = new DateTime(2026, 10, 4, 10, 0, 0, DateTimeKind.Utc);

        await Store.MarkSentAsync([sent.Id], first, ct);
        await Store.MarkSentAsync([sent.Id], first.AddHours(1), ct);

        (await Store.GetAsync(sent.Id, ct))!.SentAt.Should().Be(first);
        (await Store.ListForSessionAsync(sessionId, ct)).Single(a => a.Id == sent.Id).SentAt.Should().Be(first);
        (await Store.DeleteUnsentAsync(sent.Id, ct)).Should().BeFalse();
        (await Store.DeleteUnsentAsync(unsent.Id, ct)).Should().BeTrue();
        (await Store.GetAsync(unsent.Id, ct)).Should().BeNull();
    }
}
