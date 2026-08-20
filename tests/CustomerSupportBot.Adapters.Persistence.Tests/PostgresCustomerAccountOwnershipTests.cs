// Müşteri hesabı ↔ müşteri kaydı bağının GERÇEK Postgres'e karşı doğrulanması.
//
// İki koruma da yalnızca gerçek veritabanında anlamlı: e-posta sahiplik sorgusu SQL'e
// çevrilerek çalışıyor (in-memory LINQ'te karşılaştırma semantiği farklı olabilir), filtreli
// unique index ise tamamen Postgres'e ait bir kısıt — EF InMemory provider'da hiç var olmaz,
// dolayısıyla orada bu hatanın aranabileceği bir ortam yoktur.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Auth;
using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresCustomerAccountOwnershipTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresCustomerAccountOwnershipTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private CustomerRepository NewRepo() => new(_fixture.DbFactory);

    /// <summary>Testler paralel/tekrarlı koştuğunda çakışmasın diye her çağrı kendi müşterisini yaratır.</summary>
    private async Task<long> NewCustomerAsync(string? email)
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        var entity = new CustomerEntity
        {
            Id = Random.Shared.NextInt64(900_000, 9_000_000),
            FullName = "Sahiplik Testi",
            Email = email,
        };
        ctx.Customers.Add(entity);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return entity.Id;
    }

    [Fact]
    public async Task IsEmailOwned_ReturnsTrue_ForTheCustomersOwnEmail()
    {
        var id = await NewCustomerAsync("sahip@example.com");

        (await NewRepo().IsEmailOwnedByCustomerAsync(id, "sahip@example.com",
            TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task IsEmailOwned_ReturnsFalse_ForADifferentEmail()
    {
        var id = await NewCustomerAsync("sahip@example.com");

        (await NewRepo().IsEmailOwnedByCustomerAsync(id, "baskasi@example.com",
            TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData("SAHIP@EXAMPLE.COM")]
    [InlineData("  sahip@example.com  ")]
    public async Task IsEmailOwned_IgnoresCaseAndSurroundingWhitespace(string input)
    {
        var id = await NewCustomerAsync("sahip@example.com");

        (await NewRepo().IsEmailOwnedByCustomerAsync(id, input,
            TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    /// <summary>
    /// E-postası olmayan müşteri hiçbir e-postayla eşleşmemeli. Ters davranış burada özellikle
    /// tehlikeli olurdu: e-postasız müşteri kayıtları herkese açık bir kayıt yolu olurdu.
    /// </summary>
    [Fact]
    public async Task IsEmailOwned_ReturnsFalse_WhenCustomerHasNoEmail()
    {
        var id = await NewCustomerAsync(null);

        (await NewRepo().IsEmailOwnedByCustomerAsync(id, "herhangi@example.com",
            TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task IsEmailOwned_ReturnsFalse_WhenCustomerDoesNotExist()
    {
        (await NewRepo().IsEmailOwnedByCustomerAsync(-1, "sahip@example.com",
            TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    /// <summary>
    /// Uygulama katmanındaki kontrol yarış koşulunda aşılabilir (iki eşzamanlı kayıt, ikisi de
    /// kontrolü geçer). Son söz veritabanınındır: bir müşteriye ikinci hesap bağlanamamalı.
    /// </summary>
    [Fact]
    public async Task ASecondAccountCannotBeLinkedToTheSameCustomer()
    {
        var customerId = await NewCustomerAsync("sahip@example.com");

        await using var ctx = _fixture.DbFactory.CreateDbContext();
        ctx.Users.Add(NewUser(customerId));
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var ctx2 = _fixture.DbFactory.CreateDbContext();
        ctx2.Users.Add(NewUser(customerId));

        var act = async () => await ctx2.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>
    /// Index filtreli: müşteriye bağlı OLMAYAN kullanıcılar (admin/agent) sınırsız sayıda
    /// NULL taşıyabilmeli. Filtresiz bir unique index ikinci admin kullanıcısını reddederdi.
    /// </summary>
    [Fact]
    public async Task MultipleStaffUsersWithoutACustomerLinkAreAllowed()
    {
        await using var ctx = _fixture.DbFactory.CreateDbContext();
        ctx.Users.Add(NewUser(null));
        ctx.Users.Add(NewUser(null));

        var act = async () => await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    private static UserEntity NewUser(long? linkedCustomerId) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Username = $"u-{Guid.NewGuid():N}@example.com",
        PasswordHash = "hash",
        Role = linkedCustomerId is null ? "Admin" : "Customer",
        LinkedCustomerId = linkedCustomerId?.ToString(),
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
    };
}
