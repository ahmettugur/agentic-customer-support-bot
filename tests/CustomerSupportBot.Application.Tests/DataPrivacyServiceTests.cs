// Kişisel veri (KVKK): saklama süresi temizliği, müşterinin verisini dışa aktarma ve silme.

using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Inbound;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Privacy;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Application.Tests;

public class DataPrivacyServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Çağrıları kaydeden, istenirse hata veren silici.</summary>
    private sealed class RecordingEraser(string name, bool fail = false) : ISessionDataEraser, ICustomerDataEraser
    {
        public string Name { get; } = name;
        public List<string> ErasedSessions { get; } = [];
        public List<string> ErasedCustomers { get; } = [];
        public bool Fail { get; set; } = fail;

        public Task<int> EraseSessionsAsync(IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("depo kapalı");
            ErasedSessions.AddRange(sessionIds);
            return Task.FromResult(sessionIds.Count);
        }

        public Task<int> EraseCustomerAsync(string customerId, CancellationToken ct = default)
        {
            if (Fail) throw new InvalidOperationException("depo kapalı");
            ErasedCustomers.Add(customerId);
            return Task.FromResult(1);
        }
    }

    private sealed class Harness
    {
        public required DataPrivacyService Service { get; init; }
        public required InMemorySessionManager Sessions { get; init; }
        public required InMemoryAttachmentStore Attachments { get; init; }
        public required InMemoryCustomerProfileStore Profiles { get; init; }
        public required RecordingEraser Eraser { get; init; }
        public required IRatingStore Ratings { get; init; }
        public required IApprovalQueue Approvals { get; init; }
        public required IOrderRepository Orders { get; init; }
        public required IComplaintRepository Complaints { get; init; }
    }

    private static Harness Build(DataRetentionOptions? options = null, RecordingEraser? eraser = null)
    {
        var locks = new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
        var sessions = new InMemorySessionManager(locks);
        var attachments = new InMemoryAttachmentStore();
        var profiles = new InMemoryCustomerProfileStore();
        eraser ??= new RecordingEraser("test-store");
        var ratings = Substitute.For<IRatingStore>();
        var approvals = Substitute.For<IApprovalQueue>();
        approvals.GetHistoryForCustomerAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ApprovalRequest>());
        var orders = Substitute.For<IOrderRepository>();
        orders.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, OrderInfo)>());
        var complaints = Substitute.For<IComplaintRepository>();
        complaints.GetByCustomer(Arg.Any<string>()).Returns(Array.Empty<(string, ComplaintInfo)>());

        var service = new DataPrivacyService(
            sessions, attachments,
            [attachments, eraser], [eraser],
            profiles, ratings, approvals, orders, complaints,
            Options.Create(options ?? new DataRetentionOptions()),
            NullLogger<DataPrivacyService>.Instance);

        return new Harness
        {
            Service = service, Sessions = sessions, Attachments = attachments, Profiles = profiles,
            Eraser = eraser, Ratings = ratings, Approvals = approvals, Orders = orders, Complaints = complaints
        };
    }

    private static async Task<AgentSession> SessionAsync(Harness h, string? customerId, DateTime lastActivityUtc)
    {
        var s = await h.Sessions.GetOrCreateAsync(null);
        s.State.AuthenticatedCustomerId = customerId;
        await h.Sessions.AddExchangeAsync(s.SessionId, $"merhaba ({customerId})", "Size nasıl yardımcı olabilirim?");
        s.LastActivity = lastActivityUtc.ToLocalTime();
        return s;
    }

    private static async Task<ChatAttachment> PhotoAsync(Harness h, AgentSession s, string? customerId, DateTime createdUtc)
    {
        var a = new ChatAttachment
        {
            SessionId = s.SessionId, CustomerId = customerId, ContentType = "image/jpeg",
            Data = [1, 2, 3], Description = "kupa", CreatedAt = createdUtc
        };
        await h.Attachments.SaveAsync(a);
        return a;
    }

    // ─── Saklama süresi ──────────────────────────────────────────────────────

    [Fact]
    public async Task Retention_Disabled_DeletesNothing()
    {
        var h = Build(new DataRetentionOptions { Enabled = false });
        var old = await SessionAsync(h, "1001", Now.AddDays(-400));

        var result = await h.Service.RunRetentionAsync(Now, TestContext.Current.CancellationToken);

        result.Enabled.Should().BeFalse();
        (await h.Sessions.GetAsync(old.SessionId, TestContext.Current.CancellationToken)).Should().NotBeNull();
        h.Eraser.ErasedSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Retention_ErasesOnlyInactiveSessions_AndOldPhotos()
    {
        var h = Build(new DataRetentionOptions { ConversationRetentionDays = 180, AttachmentRetentionDays = 90 });
        var old = await SessionAsync(h, "1001", Now.AddDays(-181));
        var fresh = await SessionAsync(h, "1001", Now.AddDays(-10));
        var oldPhoto = await PhotoAsync(h, fresh, "1001", Now.AddDays(-91));
        var freshPhoto = await PhotoAsync(h, fresh, "1001", Now.AddDays(-5));

        var result = await h.Service.RunRetentionAsync(Now, TestContext.Current.CancellationToken);

        result.SessionsErased.Should().Be(1);
        result.AttachmentsDeleted.Should().Be(1);
        result.Failures.Should().BeEmpty();
        (await h.Sessions.GetAsync(old.SessionId, TestContext.Current.CancellationToken)).Should().BeNull();
        (await h.Sessions.GetAsync(fresh.SessionId, TestContext.Current.CancellationToken)).Should().NotBeNull();
        h.Eraser.ErasedSessions.Should().Equal(old.SessionId);
        (await h.Attachments.GetAsync(oldPhoto.Id, TestContext.Current.CancellationToken)).Should().BeNull("fotoğraf saklama süresi oturumdan bağımsız");
        (await h.Attachments.GetAsync(freshPhoto.Id, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task Retention_ErasesAtMostMaxSessionsPerSweep_OldestFirst()
    {
        var h = Build(new DataRetentionOptions { ConversationRetentionDays = 30, MaxSessionsPerSweep = 2 });
        var oldest = await SessionAsync(h, null, Now.AddDays(-300));
        var older = await SessionAsync(h, null, Now.AddDays(-200));
        var old = await SessionAsync(h, null, Now.AddDays(-100));

        var result = await h.Service.RunRetentionAsync(Now, TestContext.Current.CancellationToken);

        result.SessionsErased.Should().Be(2);
        h.Eraser.ErasedSessions.Should().BeEquivalentTo([oldest.SessionId, older.SessionId]);
        (await h.Sessions.GetAsync(old.SessionId, TestContext.Current.CancellationToken)).Should().NotBeNull("bir sonraki taramaya kalır");
    }

    [Fact]
    public async Task Retention_WhenAStoreFails_SessionsStayForTheNextSweep()
    {
        var h = Build(new DataRetentionOptions { ConversationRetentionDays = 30 }, new RecordingEraser("traces", fail: true));
        var old = await SessionAsync(h, "1001", Now.AddDays(-100));

        var result = await h.Service.RunRetentionAsync(Now, TestContext.Current.CancellationToken);

        result.SessionsErased.Should().Be(0);
        result.Failures.Should().ContainSingle().Which.Should().Contain("traces");
        (await h.Sessions.GetAsync(old.SessionId, TestContext.Current.CancellationToken)).Should().NotBeNull(
            "oturum en son silinir; bir depo başarısızsa yerinde kalır ve sonraki taramada tekrar denenir");
    }

    [Fact]
    public async Task Retention_ZeroDays_TurnsThatCategoryOff()
    {
        var h = Build(new DataRetentionOptions { ConversationRetentionDays = 0, AttachmentRetentionDays = 0 });
        var old = await SessionAsync(h, "1001", Now.AddDays(-1000));
        var photo = await PhotoAsync(h, old, "1001", Now.AddDays(-1000));

        var result = await h.Service.RunRetentionAsync(Now, TestContext.Current.CancellationToken);

        result.SessionsErased.Should().Be(0);
        result.AttachmentsDeleted.Should().Be(0);
        (await h.Sessions.GetAsync(old.SessionId, TestContext.Current.CancellationToken)).Should().NotBeNull();
        (await h.Attachments.GetAsync(photo.Id, TestContext.Current.CancellationToken)).Should().NotBeNull();
    }

    // ─── Dışa aktarma ────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_ContainsOnlyThisCustomersData_WithoutImageBytes()
    {
        var h = Build();
        var mine = await SessionAsync(h, "1001", Now.AddDays(-1));
        var theirs = await SessionAsync(h, "2002", Now.AddDays(-1));
        await PhotoAsync(h, mine, "1001", Now.AddDays(-1));
        await PhotoAsync(h, theirs, "2002", Now.AddDays(-1));
        await h.Profiles.GetOrCreateAsync("1001");
        h.Ratings.GetBySession(mine.SessionId).Returns(new ConversationRating { SessionId = mine.SessionId, Stars = 5, Feedback = "iyi" });
        h.Orders.GetByCustomer("1001").Returns([("10248", new OrderInfo { CustomerId = "1001", Status = "Shipped", Lines = [new("Chai", 2)] })]);
        h.Complaints.GetByCustomer("1001").Returns([("C1", new ComplaintInfo { OrderId = "10248", CustomerId = "1001", Complaint = "kırık", Status = "Açık" })]);
        h.Approvals.GetHistoryForCustomerAsync("1001", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new ApprovalRequest { Id = "a1", ToolName = "return_request_tool", CustomerId = "1001" }]);

        var export = await h.Service.ExportCustomerDataAsync("1001", TestContext.Current.CancellationToken);

        export.CustomerId.Should().Be("1001");
        export.Profile.Should().NotBeNull();
        export.Sessions.Should().ContainSingle().Which.SessionId.Should().Be(mine.SessionId);
        export.Sessions[0].Messages.Should().Contain(m => m.Text == "merhaba (1001)");
        export.Sessions[0].Attachments.Should().ContainSingle().Which.Description.Should().Be("kupa");
        export.Ratings.Should().ContainSingle().Which.Stars.Should().Be(5);
        export.Orders.Should().ContainSingle().Which.Lines.Should().Contain("Chai");
        export.Complaints.Should().ContainSingle().Which.Complaint.Should().Be("kırık");
        export.Approvals.Should().ContainSingle().Which.ToolName.Should().Be("return_request_tool");
        System.Text.Json.JsonSerializer.Serialize(export).Should().NotContain("2002");
    }

    // ─── Silme ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Erase_RemovesSessionsPhotosProfileAndCustomerMemory_ButNotOtherCustomers()
    {
        var h = Build();
        var mine = await SessionAsync(h, "1001", Now.AddDays(-1));
        var theirs = await SessionAsync(h, "2002", Now.AddDays(-1));
        var myPhoto = await PhotoAsync(h, mine, "1001", Now);
        var theirPhoto = await PhotoAsync(h, theirs, "2002", Now);
        await h.Profiles.GetOrCreateAsync("1001");
        await h.Profiles.GetOrCreateAsync("2002");

        var result = await h.Service.EraseCustomerDataAsync("1001", TestContext.Current.CancellationToken);

        result.SessionsErased.Should().Be(1);
        result.RecordsByStore.Should().ContainKey("test-store");
        (await h.Sessions.GetAsync(mine.SessionId, TestContext.Current.CancellationToken)).Should().BeNull();
        (await h.Sessions.GetAsync(theirs.SessionId, TestContext.Current.CancellationToken)).Should().NotBeNull();
        (await h.Attachments.GetAsync(myPhoto.Id, TestContext.Current.CancellationToken)).Should().BeNull();
        (await h.Attachments.GetAsync(theirPhoto.Id, TestContext.Current.CancellationToken)).Should().NotBeNull();
        h.Profiles.Get("1001").Should().BeNull();
        h.Profiles.Get("2002").Should().NotBeNull();
        h.Eraser.ErasedSessions.Should().Equal(mine.SessionId);
        h.Eraser.ErasedCustomers.Should().Equal("1001");
    }

    [Fact]
    public async Task Erase_PartialFailure_ThrowsWithTheStoreName_AndKeepsSessionsForRetry()
    {
        var eraser = new RecordingEraser("escalations", fail: true);
        var h = Build(eraser: eraser);
        var mine = await SessionAsync(h, "1001", Now.AddDays(-1));

        var act = () => h.Service.EraseCustomerDataAsync("1001");

        (await act.Should().ThrowAsync<DataErasureException>())
            .Which.FailedStores.Should().Contain("escalations");
        (await h.Sessions.GetAsync(mine.SessionId, TestContext.Current.CancellationToken)).Should().NotBeNull();

        eraser.Fail = false;
        var retry = await h.Service.EraseCustomerDataAsync("1001", TestContext.Current.CancellationToken);
        retry.SessionsErased.Should().Be(1);
        (await h.Sessions.GetAsync(mine.SessionId, TestContext.Current.CancellationToken)).Should().BeNull();
    }
}
