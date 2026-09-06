using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Personalization;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class ProfileConsolidationConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consolidation_DoesNotReplaceNewerFieldsOrRecreateDeletedProfile(bool delete)
    {
        var store = new InMemoryCustomerProfileStore();
        store.Upsert(new CustomerProfile { CustomerId = "1001", TotalTurns = 1 });
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chat = Substitute.For<IGeneralChatClient>();
        chat.CompleteAsync(Arg.Any<IReadOnlyList<ConversationMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { started.SetResult(); return release.Task; });
        var service = new CustomerProfileService(store, chat,
            new InMemoryDistributedLock(Options.Create(new RedisOptions())), Substitute.For<IProductCatalogRepository>(),
            NullLogger<CustomerProfileService>.Instance);

        var pending = service.ConsolidateAsync("1001", TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (delete) store.Delete("1001");
        else store.Upsert(new CustomerProfile { CustomerId = "1001", TotalTurns = 3, AdminNote = "new note" });
        release.SetResult("{\"summary\":\"new summary\",\"preferredTone\":\"formal\",\"traits\":[]}");
        var result = await pending;

        if (delete)
        {
            result.Should().BeNull();
            store.Get("1001").Should().BeNull();
        }
        else
        {
            result!.TotalTurns.Should().Be(3);
            result.AdminNote.Should().Be("new note");
            result.Summary.Should().Be("new summary");
        }
    }
}
