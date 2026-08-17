using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Adapters.Persistence.InMemory;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemoryCustomerProfileStoreTests
{
    [Fact]
    public void GetOrCreate_ReturnsSameInstanceForSameId()
    {
        var store = new InMemoryCustomerProfileStore();
        var a = store.GetOrCreate("CUST-1");
        var b = store.GetOrCreate("CUST-1");

        a.Should().BeSameAs(b);
        store.Count.Should().Be(1);
    }

    [Fact]
    public void GetOrCreate_BlankCustomerId_Throws()
    {
        var store = new InMemoryCustomerProfileStore();
        Action act = () => store.GetOrCreate("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Get("CUST-X").Should().BeNull();
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var store = new InMemoryCustomerProfileStore();
        store.GetOrCreate("CUST-1");
        store.Get("cust-1").Should().NotBeNull();
    }

    [Fact]
    public void Upsert_BlankCustomerId_Throws()
    {
        var store = new InMemoryCustomerProfileStore();
        Action act = () => store.Upsert(new CustomerProfile { CustomerId = "" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Delete("CUST-X").Should().BeFalse();
    }

    [Fact]
    public void List_OrdersByLastInteractionDescending()
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile { CustomerId = "A", LastInteractionAt = DateTime.UtcNow.AddMinutes(-5) });
        store.Upsert(new CustomerProfile { CustomerId = "B", LastInteractionAt = DateTime.UtcNow });
        store.Upsert(new CustomerProfile { CustomerId = "C", LastInteractionAt = DateTime.UtcNow.AddMinutes(-1) });

        var ids = store.List().Select(p => p.CustomerId).ToArray();
        ids.Should().Equal("B", "C", "A");
    }

    [Fact]
    public void List_RespectsTakeArgument()
    {
        var store = new InMemoryCustomerProfileStore();
        for (int i = 0; i < 5; i++)
            store.Upsert(new CustomerProfile { CustomerId = $"C-{i}", LastInteractionAt = DateTime.UtcNow.AddSeconds(i) });

        store.List(take: 3).Should().HaveCount(3);
    }
}
