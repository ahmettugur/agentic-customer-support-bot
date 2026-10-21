// IMessageFeedbackStore sözleşmesi: bellek içi ve Postgres uygulaması AYNI testlerden geçmeli.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public abstract class MessageFeedbackStoreContract
{
    protected abstract IMessageFeedbackStore Create();
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;
    protected static string NewSession() => "s-" + Guid.NewGuid().ToString("N");

    protected static MessageFeedback Fb(string session, string text, bool helpful, string? reason = null, string? comment = null,
        string? trace = null, DateTime? at = null) => new()
    {
        SessionId = session, MessageHash = MessageFeedback.HashOf(text), Helpful = helpful, Reason = reason, Comment = comment,
        TraceId = trace, CustomerId = "1027", CreatedAt = at ?? DateTime.UtcNow, UpdatedAt = at ?? DateTime.UtcNow
    };

    [Fact]
    public async Task Upsert_RoundTrips()
    {
        var store = Create();
        var s = NewSession();

        await store.UpsertAsync(Fb(s, "yanıt", helpful: false, reason: "wrong", comment: "fiyat yanlış", trace: "t-1"), Ct);
        var got = (await store.GetForSessionAsync(s, Ct)).Single();

        Assert.False(got.Helpful);
        Assert.Equal("wrong", got.Reason);
        Assert.Equal("fiyat yanlış", got.Comment);
        Assert.Equal("t-1", got.TraceId);
        Assert.Equal("1027", got.CustomerId);
        Assert.Equal(MessageFeedback.HashOf("yanıt"), got.MessageHash);
    }

    [Fact]
    public async Task Upsert_SameMessageTwice_UpdatesInsteadOfDuplicating_AndKeepsCreatedAt()
    {
        var store = Create();
        var s = NewSession();
        var first = DateTime.UtcNow.AddMinutes(-5);
        await store.UpsertAsync(Fb(s, "yanıt", true, at: first), Ct);

        await store.UpsertAsync(Fb(s, "yanıt", false, reason: "unclear", at: DateTime.UtcNow), Ct);

        var got = (await store.GetForSessionAsync(s, Ct)).Single();
        Assert.False(got.Helpful);
        Assert.Equal("unclear", got.Reason);
        Assert.True((got.CreatedAt - first).Duration() < TimeSpan.FromSeconds(1), "ilk oluşturma zamanı korunmalı");
        Assert.True(got.UpdatedAt > got.CreatedAt);
    }

    [Fact]
    public async Task DifferentMessages_AreSeparateRows()
    {
        var store = Create();
        var s = NewSession();

        await store.UpsertAsync(Fb(s, "birinci", true), Ct);
        await store.UpsertAsync(Fb(s, "ikinci", false), Ct);

        Assert.Equal(2, (await store.GetForSessionAsync(s, Ct)).Count);
    }

    [Fact]
    public async Task Remove_DeletesOnlyThatMessage_AndUnknownIsNotAnError()
    {
        var store = Create();
        var s = NewSession();
        await store.UpsertAsync(Fb(s, "birinci", true), Ct);
        await store.UpsertAsync(Fb(s, "ikinci", true), Ct);

        await store.RemoveAsync(s, MessageFeedback.HashOf("birinci"), Ct);
        await store.RemoveAsync(s, "yok", Ct);

        Assert.Equal([MessageFeedback.HashOf("ikinci")], (await store.GetForSessionAsync(s, Ct)).Select(f => f.MessageHash));
    }

    [Fact]
    public async Task GetForSession_IsScopedPerSession()
    {
        var store = Create();
        var a = NewSession();
        var b = NewSession();
        await store.UpsertAsync(Fb(a, "x", true), Ct);

        Assert.Empty(await store.GetForSessionAsync(b, Ct));
    }

    [Fact]
    public async Task GetRecent_FiltersByDirection_NewestFirst_AndHonorsCount()
    {
        var store = Create();
        var s = NewSession();
        await store.UpsertAsync(Fb(s, "a", false, at: DateTime.UtcNow.AddMinutes(-3)), Ct);
        await store.UpsertAsync(Fb(s, "b", true, at: DateTime.UtcNow.AddMinutes(-2)), Ct);
        await store.UpsertAsync(Fb(s, "c", false, at: DateTime.UtcNow.AddMinutes(-1)), Ct);

        var down = (await store.GetRecentAsync(1000, helpful: false, Ct)).Where(f => f.SessionId == s).ToList();
        var oneOnly = await store.GetRecentAsync(1, helpful: null, Ct);

        Assert.Equal([MessageFeedback.HashOf("c"), MessageFeedback.HashOf("a")], down.Select(f => f.MessageHash));
        Assert.Single(oneOnly);
    }

    [Fact]
    public async Task GetUnhelpfulSince_ReturnsOnlyDownvotesAfterTheCutoff()
    {
        var store = Create();
        var s = NewSession();
        await store.UpsertAsync(Fb(s, "eski 👎", false, at: DateTime.UtcNow.AddDays(-10)), Ct);
        await store.UpsertAsync(Fb(s, "yeni 👎", false, at: DateTime.UtcNow.AddMinutes(-1)), Ct);
        await store.UpsertAsync(Fb(s, "yeni 👍", true, at: DateTime.UtcNow.AddMinutes(-1)), Ct);

        var got = (await store.GetUnhelpfulSinceAsync(DateTime.UtcNow.AddDays(-1), 1000, Ct)).Where(f => f.SessionId == s).ToList();

        Assert.Equal([MessageFeedback.HashOf("yeni 👎")], got.Select(f => f.MessageHash));
    }
}
