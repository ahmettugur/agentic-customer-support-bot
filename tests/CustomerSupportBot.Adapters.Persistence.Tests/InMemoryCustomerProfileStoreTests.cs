using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryCustomerProfileStoreTests
{
    [Fact]
    public async Task GetOrCreate_ReturnsSameInstanceForSameId()
    {
        var store = new InMemoryCustomerProfileStore();
        var a = await store.GetOrCreateAsync("CUST-1");
        var b = await store.GetOrCreateAsync("CUST-1");

        a.Should().BeSameAs(b);
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetOrCreate_BlankCustomerId_Throws()
    {
        var store = new InMemoryCustomerProfileStore();
        Func<Task> act = () => store.GetOrCreateAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Get("CUST-X").Should().BeNull();
    }

    [Fact]
    public async Task Get_IsCaseInsensitive()
    {
        var store = new InMemoryCustomerProfileStore();
        await store.GetOrCreateAsync("CUST-1");
        store.Get("cust-1").Should().NotBeNull();
    }

    [Fact]
    public async Task Upsert_BlankCustomerId_Throws()
    {
        var store = new InMemoryCustomerProfileStore();
        Func<Task> act = () => store.UpsertAsync(new CustomerProfile { CustomerId = "" });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsFalse()
    {
        var store = new InMemoryCustomerProfileStore();
        (await store.DeleteAsync("CUST-X")).Should().BeFalse();
    }

    [Fact]
    public async Task List_OrdersByLastInteractionDescending()
    {
        var store = new InMemoryCustomerProfileStore();
        await store.UpsertAsync(new CustomerProfile { CustomerId = "A", LastInteractionAt = DateTime.UtcNow.AddMinutes(-5) });
        await store.UpsertAsync(new CustomerProfile { CustomerId = "B", LastInteractionAt = DateTime.UtcNow });
        await store.UpsertAsync(new CustomerProfile { CustomerId = "C", LastInteractionAt = DateTime.UtcNow.AddMinutes(-1) });

        var ids = store.List().Select(p => p.CustomerId).ToArray();
        ids.Should().Equal("B", "C", "A");
    }

    [Fact]
    public async Task List_RespectsTakeArgument()
    {
        var store = new InMemoryCustomerProfileStore();
        for (int i = 0; i < 5; i++)
            await store.UpsertAsync(new CustomerProfile { CustomerId = $"C-{i}", LastInteractionAt = DateTime.UtcNow.AddSeconds(i) });

        store.List(take: 3).Should().HaveCount(3);
    }
}
