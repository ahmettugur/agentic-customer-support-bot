// Tests/Services/PostgresEscalationConcurrencyTests.cs
//
// Eskalasyon kuyruğunun eşzamanlılık garantileri GERÇEK Postgres'e karşı.
//
// İki "pod" iki ayrı PostgresEscalationSink örneğidir; her biri AYRI bir mesaj veri yoluna
// bağlıdır, yani pub/sub senkronizasyonu yoktur — pod'lar arası mesajın henüz ulaşmadığı
// (ya da hiç ulaşmadığı) en kötü durum. Garantiler yalnızca DB'den gelmelidir:
//   - Session + ajan başına tek açık eskalasyon (ux_escalations_open_session_agent).
//   - Aynı kaydı yarışan iki karardan yalnızca biri uygulanır (koşullu UPDATE).

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresEscalationConcurrencyTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresEscalationConcurrencyTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private PostgresEscalationSink NewPod() => new(
        _fixture.DbFactory,
        new InMemoryMessageBusHub().CreateNode(),
        NullLogger<PostgresEscalationSink>.Instance);

    private static EscalationRequest NewReq(string sessionId, string agent = "ComplaintAgent") => new()
    {
        Id = Guid.NewGuid().ToString("N")[..12],
        SessionId = sessionId,
        AgentName = agent,
        UserQuery = "şikayet",
        Reason = "needs human"
    };

    private async Task<List<string>> OpenRowStatusesAsync(string sessionId, CancellationToken ct)
    {
        await using var ctx = await _fixture.DbFactory.CreateDbContextAsync(ct);
        return await ctx.Escalations.AsNoTracking()
            .Where(e => e.SessionId == sessionId && (e.Status == "Open" || e.Status == "Acknowledged"))
            .Select(e => e.Id)
            .ToListAsync(ct);
    }

    [Fact]
    public async Task Create_SecondPodWithStaleCache_GetsExistingInsteadOfDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var podA = NewPod();
        var podB = NewPod();
        await podA.WarmUpAsync(ct);
        await podB.WarmUpAsync(ct);   // B'nin cache'i A'nın kaydını hiç görmeyecek

        var created = await podA.CreateAsync(NewReq(sessionId));

        var raisedOnB = 0;
        podB.RequestCreated += (_, _) => raisedOnB++;
        var fromB = await podB.CreateAsync(NewReq(sessionId));

        fromB.Id.Should().Be(created.Id, "kısıt ihlalinde kazanan kayıt döndürülmeli");
        raisedOnB.Should().Be(0, "yeni kayıt oluşmadı — RequestCreated tetiklenmemeli");
        (await OpenRowStatusesAsync(sessionId, ct)).Should().ContainSingle();
        podB.Get(created.Id).Should().NotBeNull("kaybeden pod mevcut kaydı cache'ine almalı");
    }

    [Fact]
    public async Task Create_ConcurrentFromManyPods_LeavesExactlyOneOpenRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var pods = Enumerable.Range(0, 6).Select(_ => NewPod()).ToList();
        foreach (var pod in pods) await pod.WarmUpAsync(ct);

        var results = await Task.WhenAll(pods.Select(pod => Task.Run(() => pod.CreateAsync(NewReq(sessionId)), ct)));

        var openIds = await OpenRowStatusesAsync(sessionId, ct);
        openIds.Should().ContainSingle();
        results.Select(r => r.Id).Should().OnlyContain(id => id == openIds[0]);
    }

    [Fact]
    public async Task Create_SameSessionDifferentAgent_IsNotDeduplicated()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var pod = NewPod();

        var complaint = await pod.CreateAsync(NewReq(sessionId, "ComplaintAgent"));
        var order = await pod.CreateAsync(NewReq(sessionId, "OrderAgent"));

        order.Id.Should().NotBe(complaint.Id);
        (await OpenRowStatusesAsync(sessionId, ct)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Create_AfterPreviousResolved_OpensNewOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var pod = NewPod();

        var first = await pod.CreateAsync(NewReq(sessionId));
        (await pod.DecideAsync(first.Id, "resolve", "admin", "ok")).Should().BeTrue();

        var second = await pod.CreateAsync(NewReq(sessionId));

        second.Id.Should().NotBe(first.Id, "kısıt yalnızca AÇIK kayıtları kapsar");
        (await OpenRowStatusesAsync(sessionId, ct)).Should().Equal(second.Id);
    }

    [Fact]
    public async Task Decide_StaleCacheOnOtherPod_DoesNotOverwriteFirstDecision()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var podA = NewPod();
        var created = await podA.CreateAsync(NewReq(sessionId));

        var podB = NewPod();
        await podB.WarmUpAsync(ct);   // B kaydı "Open" olarak görür
        podB.Get(created.Id)!.Status.Should().Be(EscalationStatus.Open);

        (await podA.DecideAsync(created.Id, "resolve", "alice", "çözüldü")).Should().BeTrue();

        var decidedOnB = 0;
        podB.RequestDecided += (_, _) => decidedOnB++;
        (await podB.DecideAsync(created.Id, "dismiss", "bob", "geçersiz")).Should()
            .BeFalse("B'nin gördüğü durum artık geçerli değil — karar uygulanmamalı");
        decidedOnB.Should().Be(0);

        await using var ctx = await _fixture.DbFactory.CreateDbContextAsync(ct);
        var row = await ctx.Escalations.AsNoTracking().SingleAsync(e => e.Id == created.Id, ct);
        row.Status.Should().Be("Resolved");
        row.AssignedTo.Should().Be("alice");
        row.Resolution.Should().Be("çözüldü");

        podB.Get(created.Id)!.Status.Should().Be(EscalationStatus.Resolved,
            "yarışı kaybeden pod cache'ini DB'nin gerçeğiyle tazelemeli");
    }

    [Fact]
    public async Task Decide_ConcurrentFromManyPods_ExactlyOneSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var created = await NewPod().CreateAsync(NewReq(sessionId));

        var pods = Enumerable.Range(0, 6).Select(_ => NewPod()).ToList();
        foreach (var pod in pods) await pod.WarmUpAsync(ct);

        var outcomes = await Task.WhenAll(pods.Select((pod, i) => Task.Run(() =>
            pod.DecideAsync(created.Id, i % 2 == 0 ? "resolve" : "dismiss", $"admin{i}", $"r{i}"), ct)));

        outcomes.Count(o => o).Should().Be(1);
    }

    [Fact]
    public async Task Decide_LocalFailure_DoesNotMutateCachedObject()
    {
        var ct = TestContext.Current.CancellationToken;
        var sessionId = $"s-{Guid.NewGuid():N}";
        var podA = NewPod();
        var created = await podA.CreateAsync(NewReq(sessionId));

        var podB = NewPod();
        await podB.WarmUpAsync(ct);
        var heldByCaller = podB.Get(created.Id)!;

        await podA.DecideAsync(created.Id, "resolve", "alice", "ok");
        await podB.DecideAsync(created.Id, "dismiss", "bob", "x");

        // State machine eskiden paylaşılan cache nesnesini doğrudan değiştiriyordu; DB'ye
        // yazılamamış bir karar okuyuculara "Dismissed/bob" olarak görünüyordu.
        heldByCaller.AssignedTo.Should().NotBe("bob");
        heldByCaller.Status.Should().NotBe(EscalationStatus.Dismissed);
    }
}
