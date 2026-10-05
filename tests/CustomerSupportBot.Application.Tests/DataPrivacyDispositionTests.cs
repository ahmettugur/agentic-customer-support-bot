// KVKK: konuşma kapanış kayıtları (temsilci notu içerir) dışa aktarmada yer alır ve silinir.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Privacy;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class DataPrivacyDispositionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (DataPrivacyService Service, InMemorySessionManager Sessions, InMemoryConversationDispositionStore Dispositions) Build()
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var attachments = new InMemoryAttachmentStore();
        var dispositions = new InMemoryConversationDispositionStore();
        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetHistoryForCustomerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ApprovalRequest>());
        var orders = Substitute.For<IOrderRepository>();
        orders.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, OrderInfo)>());
        var complaints = Substitute.For<IComplaintRepository>();
        complaints.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, ComplaintInfo)>());

        var service = new DataPrivacyService(
            sessions, attachments, [attachments, dispositions], [],
            new InMemoryCustomerProfileStore(), Substitute.For<IRatingStore>(), approvals, orders, complaints,
            Options.Create(new DataRetentionOptions()), NullLogger<DataPrivacyService>.Instance,
            dispositions);
        return (service, sessions, dispositions);
    }

    private static async Task<string> CustomerSessionAsync(InMemorySessionManager sessions, string customerId)
    {
        var s = await sessions.GetOrCreateAsync(null, Ct);
        s.State.AuthenticatedCustomerId = customerId;
        await sessions.AddExchangeAsync(s.SessionId, "merhaba", "buyrun", ct: Ct);
        return s.SessionId;
    }

    [Fact]
    public async Task Export_IncludesTheClosingRecords_OfTheCustomersSessions()
    {
        var (service, sessions, dispositions) = Build();
        var mine = await CustomerSessionAsync(sessions, "1001");
        var other = await CustomerSessionAsync(sessions, "2002");
        await dispositions.AddAsync(new ConversationDisposition
        {
            SessionId = mine, ReasonCode = "follow_up_required", Tags = ["iade"], Note = "Yarın aranacak", ClosedBy = "jane",
            ClosedAt = DateTime.UtcNow
        }, Ct);
        await dispositions.AddAsync(new ConversationDisposition { SessionId = other, ReasonCode = "resolved", ClosedAt = DateTime.UtcNow }, Ct);

        var export = await service.ExportCustomerDataAsync("1001", Ct);

        var d = export.Sessions.Single().Dispositions.Should().ContainSingle().Subject;
        d.ReasonCode.Should().Be("follow_up_required");
        d.Tags.Should().Equal("iade");
        d.Note.Should().Be("Yarın aranacak");
    }

    [Fact]
    public async Task Erase_RemovesTheClosingRecords()
    {
        var (service, sessions, dispositions) = Build();
        var sid = await CustomerSessionAsync(sessions, "1001");
        await dispositions.AddAsync(new ConversationDisposition { SessionId = sid, ReasonCode = "resolved", ClosedAt = DateTime.UtcNow }, Ct);

        await service.EraseCustomerDataAsync("1001", Ct);

        (await dispositions.ListForSessionAsync(sid, Ct)).Should().BeEmpty();
    }
}
