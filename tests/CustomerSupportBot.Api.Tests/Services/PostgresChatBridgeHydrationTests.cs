// Tests/Services/PostgresChatBridgeHydrationTests.cs
//
// Aynı hydration-flag regresyonu (bkz. PostgresSessionManagerHydrationTests) burada da
// vardı: EnsureSessionHydrated, DB hatasında flag'i geri almıyordu — geçici bir hata
// o session'ın HITL geçmişini process ömrü boyunca boş bırakıyordu.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Api.Tests.Helpers;
using CustomerSupportBot.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Api.Tests.Services;

[Collection("PostgresCatalog")]
public class PostgresChatBridgeHydrationTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresChatBridgeHydrationTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private static PostgresChatBridge NewBridge(IDbContextFactory<CustomerSupportDbContext> dbFactory)
        => new(dbFactory, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

    [Fact]
    public void GetHistory_TransientHydrationFailure_RetriesOnNextCall()
    {
        var sessionId = $"bridge-{Guid.NewGuid():N}";

        // 1. bridge instance: gerçek DB'ye bir mesaj yazar (hydrate + INSERT).
        var writer = NewBridge(_fixture.DbFactory);
        writer.PublishUserMessage(sessionId, "merhaba");

        // 2. instance: cache boş, ilk deneme yapay olarak başarısız kılınır.
        var flaky = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: 1);
        var reader = NewBridge(flaky);

        var firstAttempt = reader.GetHistory(sessionId);
        firstAttempt.Should().BeEmpty(
            "ilk deneme DB hatasıyla başarısız olmalı — bu sırada geçmiş hiç yüklenmemeli");

        var secondAttempt = reader.GetHistory(sessionId);
        secondAttempt.Should().ContainSingle(m => m.Text == "merhaba",
            "flag geri alınmadıysa ikinci deneme DB'yi hiç sorgulamaz ve geçmiş sonsuza dek boş kalırdı");
    }
}
