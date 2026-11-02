// Onay özeti, onay anından önceki mesajları gerçek şema üzerinde doğru sırayla ve doğru oturumdan okumalı.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresConversationExcerptStoreTests(PostgresCatalogFixture fixture)
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private async Task SeedAsync(string sessionId, params (string Role, string Text, int Second)[] messages)
    {
        await using var ctx = await fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        ctx.Sessions.Add(new SessionEntity { SessionId = sessionId, CreatedAt = T0, LastActivity = T0 });
        foreach (var (role, text, second) in messages)
            ctx.Messages.Add(new MessageEntity { SessionId = sessionId, Role = role, Text = text, CreatedAt = T0.AddSeconds(second) });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsTheNewestMessagesBeforeTheMoment_OldestFirst()
    {
        var session = $"excerpt-{Guid.NewGuid():N}";
        await SeedAsync(session, ("user", "m1", 1), ("assistant", "m2", 2), ("user", "m3", 3), ("assistant", "m4", 4), ("user", "sonra", 10));

        var messages = await new PostgresConversationExcerptStore(fixture.DbFactory)
            .GetBeforeAsync(session, T0.AddSeconds(5), 3, TestContext.Current.CancellationToken);

        messages.Should().Equal(
            new ConversationMessage(ConversationRoles.Assistant, "m2"),
            new ConversationMessage(ConversationRoles.User, "m3"),
            new ConversationMessage(ConversationRoles.Assistant, "m4"));
    }

    [Fact]
    public async Task OtherSessions_AreNeverIncluded()
    {
        var mine = $"excerpt-{Guid.NewGuid():N}";
        var other = $"excerpt-{Guid.NewGuid():N}";
        await SeedAsync(mine, ("user", "benim", 1));
        await SeedAsync(other, ("user", "başkasının", 2));

        var messages = await new PostgresConversationExcerptStore(fixture.DbFactory)
            .GetBeforeAsync(mine, T0.AddSeconds(5), 10, TestContext.Current.CancellationToken);

        messages.Select(m => m.Text).Should().Equal("benim");
    }
}
