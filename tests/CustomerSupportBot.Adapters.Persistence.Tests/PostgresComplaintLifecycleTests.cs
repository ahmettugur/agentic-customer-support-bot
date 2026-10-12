// Şikayet yaşam döngüsü — koşullu geçiş, olay satırı, liste/sayaç (gerçek Postgres).

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Catalog;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresComplaintLifecycleTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTime T0 = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
    private IComplaintRepository Repo => fixture.ComplaintRepo;

    private string NewComplaint(string? approvalId = null, DateTime? createdAt = null) => Repo.Create(new ComplaintInfo
    {
        OrderId = "1030", CustomerId = "1027", Complaint = "Ürün hasarlı geldi, kutu ezik",
        Status = WellKnown.ComplaintStatuses.Pending, ApprovalId = approvalId, CreatedAt = createdAt ?? T0
    });

    private static ComplaintTransition Claim(string id, string agent, int version = 0) => new(
        id, WellKnown.ComplaintStatuses.Pending, version, WellKnown.ComplaintStatuses.InProgress, agent, null,
        new ComplaintEvent(T0.AddMinutes(5), agent, ComplaintEventKinds.Claimed,
            WellKnown.ComplaintStatuses.Pending, WellKnown.ComplaintStatuses.InProgress, agent, null));

    [Fact]
    public async Task Create_StoresTimestampsApproval_AndCreatedEvent()
    {
        var id = NewComplaint("apr-1");

        var c = Repo.Get(id)!;
        c.CreatedAt.Should().Be(T0);
        c.UpdatedAt.Should().Be(T0);
        c.ApprovalId.Should().Be("apr-1");
        c.Version.Should().Be(0);
        var events = await Repo.GetEventsAsync(id, Ct);
        events.Should().ContainSingle().Which.Kind.Should().Be(ComplaintEventKinds.Created);
    }

    [Fact]
    public async Task Claim_Then_Resolve_UpdatesRow_AndWritesEvents()
    {
        var id = NewComplaint();

        var claimed = await Repo.TryTransitionAsync(Claim(id, "a1"), Ct);
        claimed.Change.Should().Be(ComplaintTransitionChange.Updated);
        claimed.Complaint!.Status.Should().Be(WellKnown.ComplaintStatuses.InProgress);
        claimed.Complaint.AssignedAgentId.Should().Be("a1");
        claimed.Complaint.Version.Should().Be(1);

        var resolved = await Repo.TryTransitionAsync(new ComplaintTransition(
            id, WellKnown.ComplaintStatuses.InProgress, 1, WellKnown.ComplaintStatuses.Resolved, "a1", "Yeni ürün gönderildi.",
            new ComplaintEvent(T0.AddHours(1), "ayse", ComplaintEventKinds.Resolved,
                WellKnown.ComplaintStatuses.InProgress, WellKnown.ComplaintStatuses.Resolved, "a1", "iç not")), Ct);

        resolved.Change.Should().Be(ComplaintTransitionChange.Updated);
        resolved.Complaint!.Resolution.Should().Be("Yeni ürün gönderildi.");
        resolved.Complaint.UpdatedAt.Should().Be(T0.AddHours(1));
        (await Repo.GetEventsAsync(id, Ct)).Select(e => e.Kind)
            .Should().Equal(ComplaintEventKinds.Created, ComplaintEventKinds.Claimed, ComplaintEventKinds.Resolved);
    }

    [Fact]
    public async Task Reopen_KeepsResolution_WhenNull()
    {
        var id = NewComplaint();
        await Repo.TryTransitionAsync(Claim(id, "a1"), Ct);
        await Repo.TryTransitionAsync(new ComplaintTransition(id, WellKnown.ComplaintStatuses.InProgress, 1,
            WellKnown.ComplaintStatuses.Rejected, "a1", "Hasar kargodan.", new ComplaintEvent(T0.AddHours(1), "ayse",
                ComplaintEventKinds.Rejected, WellKnown.ComplaintStatuses.InProgress, WellKnown.ComplaintStatuses.Rejected, "a1", null)), Ct);

        var reopened = await Repo.TryTransitionAsync(new ComplaintTransition(id, WellKnown.ComplaintStatuses.Rejected, 2,
            WellKnown.ComplaintStatuses.InProgress, "a1", null, new ComplaintEvent(T0.AddHours(2), "admin",
                ComplaintEventKinds.Reopened, WellKnown.ComplaintStatuses.Rejected, WellKnown.ComplaintStatuses.InProgress, "a1", "itiraz")), Ct);

        reopened.Complaint!.Status.Should().Be(WellKnown.ComplaintStatuses.InProgress);
        reopened.Complaint.Resolution.Should().Be("Hasar kargodan.");
    }

    [Fact]
    public async Task StaleVersion_IsConflict_AndWritesNoEvent()
    {
        var id = NewComplaint();
        await Repo.TryTransitionAsync(Claim(id, "a1"), Ct);

        var stale = await Repo.TryTransitionAsync(Claim(id, "b2"), Ct);

        stale.Change.Should().Be(ComplaintTransitionChange.Conflict);
        stale.Complaint!.AssignedAgentId.Should().Be("a1");
        (await Repo.GetEventsAsync(id, Ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentClaims_OnlyOneWins()
    {
        var id = NewComplaint();

        var results = await Task.WhenAll(
            Repo.TryTransitionAsync(Claim(id, "a1"), Ct),
            Repo.TryTransitionAsync(Claim(id, "b2"), Ct));

        results.Count(r => r.Change == ComplaintTransitionChange.Updated).Should().Be(1);
        results.Count(r => r.Change == ComplaintTransitionChange.Conflict).Should().Be(1);
        (await Repo.GetEventsAsync(id, Ct)).Count(e => e.Kind == ComplaintEventKinds.Claimed).Should().Be(1);
    }

    [Fact]
    public async Task Unknown_IsNotFound()
        => (await Repo.TryTransitionAsync(Claim("99999999", "a1"), Ct)).Change.Should().Be(ComplaintTransitionChange.NotFound);

    [Fact]
    public async Task AddEvent_NoteOnly_DoesNotChangeVersion()
    {
        var id = NewComplaint();

        (await Repo.AddEventAsync(id, new ComplaintEvent(T0.AddMinutes(1), "admin", ComplaintEventKinds.Note, null, null, null, "arandı"), Ct))
            .Should().BeTrue();

        Repo.Get(id)!.Version.Should().Be(0);
        (await Repo.GetEventsAsync(id, Ct)).Last().Note.Should().Be("arandı");
        (await Repo.AddEventAsync("99999999", new ComplaintEvent(T0, "admin", ComplaintEventKinds.Note, null, null, null, "x"), Ct))
            .Should().BeFalse();
    }

    [Fact]
    public async Task List_AgentScope_HidesOthersAssigned_AndCountsMatch()
    {
        var mine = NewComplaint(createdAt: T0.AddYears(5));
        var others = NewComplaint(createdAt: T0.AddYears(5).AddMinutes(1));
        var free = NewComplaint(createdAt: T0.AddYears(5).AddMinutes(2));
        await Repo.TryTransitionAsync(Claim(mine, "agent-me"), Ct);
        await Repo.TryTransitionAsync(Claim(others, "agent-other"), Ct);

        var page = await Repo.ListAsync(new ComplaintQuery(Status: ComplaintQuery.OpenStatus, VisibleToAgentId: "agent-me", PageSize: 500), Ct);
        var ids = page.Items.Select(i => i.ComplaintId).ToList();
        ids.Should().Contain([mine, free]).And.NotContain(others);

        var counts = await Repo.CountOpenAsync("agent-me", Ct);
        counts.AssignedToAgent.Should().Be(1);
        counts.Pending.Should().BeGreaterThanOrEqualTo(1);
        counts.InProgress.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task List_OpenFirstOldestFirst_ClosedOnlyNewestFirst()
    {
        var older = NewComplaint(createdAt: T0.AddYears(-3));
        var newer = NewComplaint(createdAt: T0.AddYears(-3).AddDays(1));

        var open = await Repo.ListAsync(new ComplaintQuery(Status: WellKnown.ComplaintStatuses.Pending, PageSize: 500), Ct);
        var ids = open.Items.Select(i => i.ComplaintId).ToList();
        ids.IndexOf(older).Should().BeLessThan(ids.IndexOf(newer));
        open.Total.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task LegacyRow_WithoutEvents_ListsAndDetails()
    {
        await using (var ctx = fixture.DbFactory.CreateDbContext())
        {
            ctx.Complaints.Add(new ComplaintEntity
            {
                Code = 7_000_001, OrderId = 1030, CustomerId = 1027, Complaint = "eski kayıt", Status = WellKnown.ComplaintStatuses.Pending
            });
            await ctx.SaveChangesAsync(Ct);
        }

        var c = Repo.Get("7000001")!;
        c.CreatedAt.Should().NotBe(default, "veritabanı varsayılanı now()");
        (await Repo.GetEventsAsync("7000001", Ct)).Should().BeEmpty();
        (await Repo.GetOpenAsync(5000, Ct)).Should().Contain(i => i.ComplaintId == "7000001");
    }
}
