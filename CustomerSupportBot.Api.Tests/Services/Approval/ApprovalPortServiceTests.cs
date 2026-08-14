// Onay kuyruğunun admin paneline giderken müşteri adıyla zenginleştirilmesini doğrular.
//
// Bu adım sessizce başarısız olabilecek türden: ad çözülemezse kart yine çizilir, sadece
// numaraya düşer. Yani bir regresyon testi olmadan "isim neden gitmiyor" sorusu ancak
// gözle fark edilir.

using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Approval;
using CustomerSupportBot.Domain.Model;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace CustomerSupportBot.Api.Tests.Services.Approval;

public class ApprovalPortServiceTests
{
    private static ApprovalRequest Request(string id, string? customerId) => new()
    {
        Id = id,
        ToolName = WellKnown.ToolNames.OrderPlacement,
        CustomerId = customerId
    };

    private static (ApprovalPortService Service, IApprovalQueue Queue, ICustomerRepository Customers) Build(
        params ApprovalRequest[] pending)
    {
        var queue = Substitute.For<IApprovalQueue>();
        queue.GetPending().Returns(pending);
        queue.GetRecent(Arg.Any<int>()).Returns(pending);

        var customers = Substitute.For<ICustomerRepository>();
        customers.GetFullNamesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<long, string> { [1027] = "Maria Anders", [1008] = "Hanna Moos" });

        return (new ApprovalPortService(queue, customers, NullLogger<ApprovalPortService>.Instance), queue, customers);
    }

    [Fact]
    public async Task GetPendingAsync_FillsCustomerName()
    {
        var (service, _, _) = Build(Request("a1", "1027"), Request("a2", "1008"));

        var result = await service.GetPendingAsync(TestContext.Current.CancellationToken);

        result.Select(r => r.CustomerName).Should().BeEquivalentTo(["Maria Anders", "Hanna Moos"]);
    }

    [Fact]
    public async Task GetPendingAsync_ResolvesAllNamesInASingleQuery()
    {
        // N+1 koruması: kart başına ayrı sorgu, kuyruk büyüdükçe paneli doğrusal yavaşlatırdı.
        var (service, _, customers) = Build(
            Request("a1", "1027"), Request("a2", "1008"), Request("a3", "1027"));

        await service.GetPendingAsync(TestContext.Current.CancellationToken);

        await customers.Received(1).GetFullNamesAsync(
            Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPendingAsync_UnknownCustomer_LeavesNameNull()
    {
        // Panel bu durumda yalnızca numarayı gösterir — kart kaybolmaz.
        var (service, _, _) = Build(Request("a1", "9999"));

        var result = await service.GetPendingAsync(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.CustomerName.Should().BeNull();
    }

    [Fact]
    public async Task GetPendingAsync_NonNumericCustomerId_IsNotQueried()
    {
        var (service, _, customers) = Build(Request("a1", "abc"), Request("a2", null));

        var result = await service.GetPendingAsync(TestContext.Current.CancellationToken);

        result.Should().OnlyContain(r => r.CustomerName == null);
        await customers.DidNotReceive().GetFullNamesAsync(
            Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPendingAsync_RepositoryThrows_StillReturnsQueue()
    {
        // Ad yalnızca görüntüleme kolaylığı; çözülemezse onay kuyruğu YİNE DE açılmalı.
        // Aksi hâlde bir müşteri tablosu arızası tüm HITL akışını durdururdu.
        var queue = Substitute.For<IApprovalQueue>();
        queue.GetPending().Returns([Request("a1", "1027")]);

        var customers = Substitute.For<ICustomerRepository>();
        customers.GetFullNamesAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyDictionary<long, string>>>(_ => throw new InvalidOperationException("db down"));

        var service = new ApprovalPortService(queue, customers, NullLogger<ApprovalPortService>.Instance);

        var result = await service.GetPendingAsync(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.CustomerName.Should().BeNull();
    }

    [Fact]
    public async Task GetRecentAsync_FillsCustomerName()
    {
        var (service, _, _) = Build(Request("a1", "1027"));

        var result = await service.GetRecentAsync(50, TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.CustomerName.Should().Be("Maria Anders");
    }
}
