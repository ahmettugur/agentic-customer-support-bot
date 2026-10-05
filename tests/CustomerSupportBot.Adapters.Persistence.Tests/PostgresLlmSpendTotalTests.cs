// Harcama limiti tohumlaması — dönem başından bu yana toplam LLM maliyeti (gerçek Postgres).

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresLlmSpendTotalTests(PostgresCatalogFixture fixture)
{
    /// <summary>
    /// Kayıtlar GEÇMİŞTE bir pencereye yazılır ve toplam önce/sonra farkıyla ölçülür: gelecekte bir pencere,
    /// "t0'dan beri" (üst sınırsız) toplayan diğer testlerin sonucuna karışıyordu. Aynı koleksiyondaki testler
    /// sıralı çalışır, fark yalnızca bu testin kayıtlarıdır.
    /// </summary>
    [Fact]
    public async Task TotalCostSince_SumsOnlyTheCallsInThePeriod()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new PostgresLlmCallUsageSink(fixture.DbFactory, NullLogger<PostgresLlmCallUsageSink>.Instance);
        var t0 = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(0, 1_000_000));
        LlmCallRecord Call(decimal cost, DateTime at) => new("gpt-x", "OpenAI", 10, 5, cost, 100, at, null);
        var before = await sink.GetTotalCostSinceAsync(t0, ct);

        await sink.RecordAsync(Call(5m, t0.AddMinutes(-1)), ct);   // dönemden önce
        await sink.RecordAsync(Call(0.25m, t0), ct);                // dahil
        await sink.RecordAsync(Call(0.5m, t0.AddMinutes(1)), ct);

        (await sink.GetTotalCostSinceAsync(t0, ct) - before).Should().Be(0.75m);
    }
}
