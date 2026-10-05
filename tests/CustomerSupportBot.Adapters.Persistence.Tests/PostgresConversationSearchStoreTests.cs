// Konuşma arama — gerçek Postgres: tek sorgu, Türkçe katlama, LIKE kaçırma, filtreler, sayfalama.
// Paylaşılan veritabanında diğer testlerin verisinden yalıtmak için benzersiz müşteri numarası ve sözcük.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Chat;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Conversations;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresConversationSearchStoreTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    private PostgresConversationSearchStore Store() => new(fixture.DbFactory);
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..16];

    private async Task<string> SessionAsync(string? customerId, DateTime lastActivity, params (string Role, string Text)[] messages)
    {
        var sid = Unique("srch-");
        await using var db = fixture.DbFactory.CreateDbContext();
        db.Sessions.Add(new SessionEntity
        {
            SessionId = sid, CreatedAt = lastActivity.AddMinutes(-5), LastActivity = lastActivity,
            StateJson = customerId is null ? "{}" : $$"""{"AuthenticatedCustomerId":"{{customerId}}"}"""
        });
        var at = lastActivity.AddMinutes(-5);
        foreach (var (role, text) in messages)
            db.Messages.Add(new MessageEntity { SessionId = sid, Role = role, Text = text, CreatedAt = at = at.AddSeconds(1) });
        await db.SaveChangesAsync(Ct);
        return sid;
    }

    private async Task DispositionAsync(string sid, string reason, params string[] tags)
    {
        await using var db = fixture.DbFactory.CreateDbContext();
        db.ConversationDispositions.Add(new ConversationDispositionEntity
        {
            Id = Guid.NewGuid().ToString("N")[..12], SessionId = sid, ReasonCode = reason, Tags = [.. tags], ClosedAt = T0
        });
        await db.SaveChangesAsync(Ct);
    }

    private static ConversationSearchCriteria Criteria(
        string? text = null, string? customer = null, DateTime? from = null, DateTime? to = null,
        string? reason = null, string? tag = null, int offset = 0, int take = 50) =>
        new(text is null ? null : TurkishText.Fold(text), customer, from, to, reason, tag, offset, take);

    [Fact]
    public async Task Text_MatchesTurkishCaseVariants_AndReturnsTheFirstMatchingMessage()
    {
        var word = Unique("kw");
        var hit = await SessionAsync(null, T0,
            ("user", "Merhaba"), ("assistant", "Buyrun"), ("user", $"Siparişim {word.ToUpperInvariant()} İADESİ ne oldu"));
        await SessionAsync(null, T0, ("user", "başka bir konu"));

        var rows = await Store().SearchAsync(Criteria(text: $"{word} iadesi"), Ct);

        var row = rows.Should().ContainSingle().Subject;
        row.SessionId.Should().Be(hit);
        row.MessageCount.Should().Be(3);
        row.MatchedText.Should().Contain("İADESİ");
    }

    [Fact]
    public async Task LikeWildcards_InTheTerm_AreLiteral()
    {
        var word = Unique("w");
        var literal = await SessionAsync(null, T0, ("user", $"kod a_{word}"));
        await SessionAsync(null, T0, ("user", $"kod ab{word}"));           // '_' joker olsaydı bu da eşleşirdi
        await SessionAsync(null, T0, ("user", $"kod a%z{word}"));

        (await Store().SearchAsync(Criteria(text: $"a_{word}"), Ct)).Select(r => r.SessionId).Should().Equal(literal);
        (await Store().SearchAsync(Criteria(text: $"a%{word}"), Ct)).Should().BeEmpty("'%' joker olsaydı a%z… eşleşirdi");
    }

    [Fact]
    public async Task Filters_Customer_Date_Reason_Tag_AndTheFirstCustomerMessageAsSnippet()
    {
        var customer = Unique("c");
        var old = await SessionAsync(customer, T0.AddDays(-10), ("assistant", "Hoş geldiniz"), ("user", "Kargom gecikti"));
        var recentA = await SessionAsync(customer, T0.AddMinutes(-1), ("user", "İade istiyorum"));
        var recentB = await SessionAsync(customer, T0, ("user", "Fatura"));
        await SessionAsync(Unique("c"), T0, ("user", "başka müşteri"));
        await DispositionAsync(old, "resolved", "kargo");
        await DispositionAsync(recentA, "follow_up_required", "iade");
        await DispositionAsync(recentA, "resolved", "iade", "vip");

        var all = await Store().SearchAsync(Criteria(customer: customer), Ct);
        all.Select(r => r.SessionId).Should().Equal(recentB, recentA, old);
        all.Single(r => r.SessionId == old).MatchedText.Should().Be("Kargom gecikti", "metin yoksa ilk müşteri mesajı");
        all.Single(r => r.SessionId == old).CustomerId.Should().Be(customer);
        var a = all.Single(r => r.SessionId == recentA);
        a.Reasons.Should().BeEquivalentTo("follow_up_required", "resolved");
        a.Tags.Should().BeEquivalentTo("iade", "vip");

        (await Store().SearchAsync(Criteria(customer: customer, from: T0.AddDays(-1)), Ct))
            .Select(r => r.SessionId).Should().Equal(recentB, recentA);
        (await Store().SearchAsync(Criteria(customer: customer, to: T0.AddDays(-1)), Ct))
            .Select(r => r.SessionId).Should().Equal(old);
        (await Store().SearchAsync(Criteria(customer: customer, reason: "follow_up_required"), Ct))
            .Select(r => r.SessionId).Should().Equal(recentA);
        (await Store().SearchAsync(Criteria(customer: customer, tag: "kargo"), Ct))
            .Select(r => r.SessionId).Should().Equal(old);
    }

    [Fact]
    public async Task Paging_UsesOffsetAndTake()
    {
        var customer = Unique("c");
        var s1 = await SessionAsync(customer, T0.AddMinutes(-3), ("user", "1"));
        var s2 = await SessionAsync(customer, T0.AddMinutes(-2), ("user", "2"));
        var s3 = await SessionAsync(customer, T0.AddMinutes(-1), ("user", "3"));

        (await Store().SearchAsync(Criteria(customer: customer, offset: 0, take: 2), Ct)).Select(r => r.SessionId).Should().Equal(s3, s2);
        (await Store().SearchAsync(Criteria(customer: customer, offset: 2, take: 2), Ct)).Select(r => r.SessionId).Should().Equal(s1);
    }
}
