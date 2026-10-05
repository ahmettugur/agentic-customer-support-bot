// Görüşme başına LLM maliyeti — gerçek Postgres: oturuma atfedilen ve atfedilmeyen maliyet ayrı hesaplanır.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Application.Ports.Outbound.Observability;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresLlmCostSummaryTests(PostgresCatalogFixture fixture)
{
    [Fact]
    public async Task Summary_SplitsAttributedCost_AndComputesAverageAndMedianPerConversation()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new PostgresLlmCallUsageSink(fixture.DbFactory, NullLogger<PostgresLlmCallUsageSink>.Instance);
        // Diğer testlerin kayıtlarından ayrışmak için gelecekte bir zaman penceresi.
        var t0 = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(0, 100_000));
        var (a, b) = ($"cost-a-{Guid.NewGuid():N}", $"cost-b-{Guid.NewGuid():N}");
        LlmCallRecord Call(decimal cost, string? session) => new("gpt-x", "OpenAI", 10, 5, cost, 100, t0.AddSeconds(1), session);

        await sink.RecordAsync(Call(0.01m, a), ct);
        await sink.RecordAsync(Call(0.02m, a), ct);
        await sink.RecordAsync(Call(0.05m, b), ct);
        await sink.RecordAsync(Call(0.10m, null), ct);

        var summary = await sink.GetCostSummaryAsync(t0, ct);

        summary.TotalCostUsd.Should().Be(0.18m);
        summary.AttributedCostUsd.Should().Be(0.08m);
        summary.SessionsWithCost.Should().Be(2);
        summary.AverageCostPerSessionUsd.Should().Be(0.04m);
        summary.MedianCostPerSessionUsd.Should().Be(0.04m, "0.03 ve 0.05'in ortası");
    }
}
