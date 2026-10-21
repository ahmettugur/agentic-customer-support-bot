// KVKK: bot yanıtlarına verilen geri bildirimler (yorum serbest metindir) dışa aktarmada yer alır ve silinir.

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

public class DataPrivacyMessageFeedbackTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (DataPrivacyService Service, InMemorySessionManager Sessions, InMemoryMessageFeedbackStore Feedback) Build()
    {
        var sessions = new InMemorySessionManager(new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })));
        var attachments = new InMemoryAttachmentStore();
        var feedback = new InMemoryMessageFeedbackStore();
        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetHistoryForCustomerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<ApprovalRequest>());
        var orders = Substitute.For<IOrderRepository>();
        orders.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, OrderInfo)>());
        var complaints = Substitute.For<IComplaintRepository>();
        complaints.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, ComplaintInfo)>());

        // Üretimde silici, DI'dan ISessionDataEraser olarak gelir; burada aynı örnek elle verilir.
        var service = new DataPrivacyService(
            sessions, attachments, [attachments, new FeedbackEraser(feedback)], [],
            new InMemoryCustomerProfileStore(), Substitute.For<IRatingStore>(), approvals, orders, complaints,
            Options.Create(new DataRetentionOptions()), NullLogger<DataPrivacyService>.Instance,
            messageFeedback: feedback);
        return (service, sessions, feedback);
    }

    private sealed class FeedbackEraser(InMemoryMessageFeedbackStore store) : ISessionDataEraser
    {
        public string Name => "message_feedback";
        public async Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
        {
            var n = 0;
            foreach (var s in sessionIds)
                foreach (var f in await store.GetForSessionAsync(s, ct)) { await store.RemoveAsync(s, f.MessageHash, ct); n++; }
            return n;
        }
    }

    private static async Task<string> CustomerSessionAsync(InMemorySessionManager sessions, string customerId)
    {
        var s = await sessions.GetOrCreateAsync(null, Ct);
        s.State.AuthenticatedCustomerId = customerId;
        await sessions.AddExchangeAsync(s.SessionId, "merhaba", "buyrun", ct: Ct);
        return s.SessionId;
    }

    [Fact]
    public async Task Export_IncludesTheCustomersFeedback_WithTheirComment_ButNotOthers()
    {
        var (service, sessions, feedback) = Build();
        var mine = await CustomerSessionAsync(sessions, "1001");
        var other = await CustomerSessionAsync(sessions, "2002");
        await feedback.UpsertAsync(new MessageFeedback { SessionId = mine, MessageHash = "h1", Helpful = false, Reason = "wrong", Comment = "adım Ali, yanlış fiyat" }, Ct);
        await feedback.UpsertAsync(new MessageFeedback { SessionId = other, MessageHash = "h2", Helpful = true }, Ct);

        var export = await service.ExportCustomerDataAsync("1001", Ct);

        var f = export.Sessions.Single().Feedback.Should().ContainSingle().Subject;
        f.Helpful.Should().BeFalse();
        f.Reason.Should().Be("wrong");
        f.Comment.Should().Be("adım Ali, yanlış fiyat");
    }

    [Fact]
    public async Task Erase_RemovesTheCustomersFeedback()
    {
        var (service, sessions, feedback) = Build();
        var sid = await CustomerSessionAsync(sessions, "1001");
        await feedback.UpsertAsync(new MessageFeedback { SessionId = sid, MessageHash = "h1", Helpful = false, Comment = "x" }, Ct);

        await service.EraseCustomerDataAsync("1001", Ct);

        (await feedback.GetForSessionAsync(sid, Ct)).Should().BeEmpty();
    }
}
