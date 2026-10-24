// Müşteri tercihleri gerçek Postgres'te kalıcıdır: yazma → yeniden okuma, güncelleme ve konsolidasyon tercihleri ezmez.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class ProfilePreferencesPersistenceTests(PostgresCatalogFixture fixture)
{
    private PostgresCustomerProfileStore NewStore() =>
        new(fixture.DbFactory, new InMemoryMessageBusHub().CreateNode(), NullLogger<PostgresCustomerProfileStore>.Instance);

    [Fact]
    public async Task Preferences_RoundTripThroughTheDatabase()
    {
        var id = Guid.NewGuid().ToString();
        var writer = NewStore();
        await writer.UpsertAsync(new CustomerProfile
        {
            CustomerId = id, TotalTurns = 1,
            Preferences = { [CustomerPreferences.ReplyStyle] = "short", [CustomerPreferences.Delivery] = "pickup" }
        });

        // Yeni bir mağaza örneği (boş önbellek) → doğrudan veritabanından okur.
        var fresh = NewStore().Get(id);
        fresh.Should().NotBeNull();
        fresh!.Preferences.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [CustomerPreferences.ReplyStyle] = "short", [CustomerPreferences.Delivery] = "pickup"
        });
    }

    [Fact]
    public async Task ARewrite_ReplacesThePreferences_AndConsolidationLeavesThemAlone()
    {
        var id = Guid.NewGuid().ToString();
        var store = NewStore();
        await store.UpsertAsync(new CustomerProfile { CustomerId = id, TotalTurns = 1, Preferences = { [CustomerPreferences.ReplyStyle] = "short" } });

        var p = (await store.GetOrCreateAsync(id));
        p.Preferences[CustomerPreferences.ReplyStyle] = "detailed";
        await store.UpsertAsync(p);
        await store.UpdateConsolidationAsync(id, "özet", "formal", [], TestContext.Current.CancellationToken);

        NewStore().Get(id)!.Preferences.Should().ContainSingle().Which.Value.Should().Be("detailed");
    }

    [Fact]
    public async Task AProfileWithoutPreferences_ReadsAsEmpty_NotNull()
    {
        var id = Guid.NewGuid().ToString();
        await NewStore().UpsertAsync(new CustomerProfile { CustomerId = id, TotalTurns = 1 });

        NewStore().Get(id)!.Preferences.Should().NotBeNull().And.BeEmpty();
    }
}
