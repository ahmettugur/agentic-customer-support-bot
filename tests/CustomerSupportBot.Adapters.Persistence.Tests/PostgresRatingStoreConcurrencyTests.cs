// Tests/Services/PostgresRatingStoreConcurrencyTests.cs
//
// Puanlama upsert'ü oku-sonra-yaz idi: aynı oturuma eşzamanlı İLK iki puanlamanın ikisi de
// "kayıt yok" görüp INSERT ediyor, kaybeden session_id birincil anahtar ihlaliyle 500
// alıyordu. Kaybeden artık kazananın satırını güncelleyerek tekrar dener.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresRatingStoreConcurrencyTests(PostgresCatalogFixture fixture)
{
    [Fact]
    public async Task ConcurrentFirstRatings_ForSameSession_AllSucceed_SingleRowRemains()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"rate-{Guid.NewGuid():N}";
        var pods = Enumerable.Range(0, 6)
            .Select(_ => new PostgresRatingStore(
                fixture.DbFactory, new InMemoryMessageBusHub().CreateNode(), NullLogger<PostgresRatingStore>.Instance))
            .ToList();
        foreach (var pod in pods) await pod.WarmUpAsync(ct);

        var submissions = pods.Select((pod, i) => Task.Run(() => pod.SubmitAsync(sessionId, i % 5 + 1, $"yorum {i}"), ct));
        var act = () => Task.WhenAll(submissions);

        await act.Should().NotThrowAsync("eşzamanlı ilk puanlama birincil anahtar ihlaliyle düşmemeli");

        await using var ctx = await fixture.DbFactory.CreateDbContextAsync(ct);
        (await ctx.Ratings.CountAsync(r => r.SessionId == sessionId, ct)).Should().Be(1);
    }
}
