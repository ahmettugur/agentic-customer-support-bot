// Tests/Services/PostgresApprovalQueueCrossPodTests.cs
//
// Bloklamayan onay modelinde (tool artık admin kararını beklemez) onay kuyruğunun çoklu
// pod davranışı. Üç ayrı arıza sınıfı burada kanıtlanıyor:
//
//   1. Kararın yayılması Tcs'e bağlı OLMAMALI — kararı vermeyen pod'da Tcs her zaman null'dır,
//      eski kod bu yüzden kararı hiç yaymıyordu (bildirim gitmiyor, "sonradan gir de gör" de
//      çalışmıyordu).
//   2. Bayat bir cache, aynı işin ikinci kez YÜRÜTÜLMESİNE yol açmamalı — distributed lock
//      yalnızca eş zamanlılığı çözer, sonradan tekrarı değil.
//   3. Bir pod bayat kaldığını fark ettiğinde kendini DB'den onarmalı.

using CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Hitl;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Ports.Outbound.Locking;
using CustomerSupportBot.Application.Ports.Outbound.Messaging;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresApprovalQueueCrossPodTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresApprovalQueueCrossPodTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    /// <summary>Yürütmenin KAÇ KEZ çalıştığını sayan router — mükerrer yürütmeyi görünür kılar.</summary>
    private sealed class CountingExecutionRouter : IApprovalExecutionRouter
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);

        public Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _count);
            return Task.FromResult(new ApprovalExecutionOutcome(true, "iade islendi"));
        }
    }

    private static IAppDistributedLock NewLock() =>
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 5 }));

    private PostgresApprovalQueue NewQueue(
        IMessageBusPort bus,
        IApprovalExecutionRouter router,
        IAppDistributedLock distributedLock,
        Microsoft.EntityFrameworkCore.IDbContextFactory<EfCore.CustomerSupportDbContext>? dbFactory = null) =>
        new(
            dbFactory ?? _fixture.DbFactory,
            Options.Create(new ApprovalOptions { StalePendingHours = 72 }),
            bus,
            distributedLock,
            router,
            NullLogger<PostgresApprovalQueue>.Instance);

    private static ApprovalRequest NewRequest(string sessionId) => new()
    {
        SessionId = sessionId,
        CustomerId = "ALFKI",
        ToolName = WellKnown.ToolNames.OrderCancel,
        AgentName = "OrderAgent",
        Parameters = new Dictionary<string, object?> { ["orderId"] = 1030 }
    };

    // ─── 1. Kararın yayılması Tcs'ten bağımsız olmalı ────────────────────────

    [Fact]
    public async Task Decide_OnOnePod_ReachesOtherPod_EvenThoughThatPodHasNoWaiter()
    {
        var hub = new InMemoryMessageBusHub();
        var sharedLock = NewLock();

        var decider = NewQueue(hub.CreateNode(), new CountingExecutionRouter(), sharedLock);

        // Okuyucu pod'un DB'si HİÇ çalışmıyor: kararı görebiliyorsa bu bilgi yalnızca
        // pub/sub üzerinden gelmiş olabilir. (Aksi hâlde test, hydration'ın DB'den doğru
        // durumu çekmesi sayesinde düzeltme olmadan da geçerdi — yani hiçbir şey kanıtlamazdı.)
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = NewQueue(hub.CreateNode(), new CountingExecutionRouter(), NewLock(), neverReachesDb);

        ApprovalRequest? notified = null;
        reader.RequestDecided += (_, r) => notified = r;

        var req = NewRequest($"sess-{Guid.NewGuid():N}");
        await decider.CreateAsync(req, TestContext.Current.CancellationToken);

        // Okuyucu pod kaydı OnRemoteCreated ile aldı ve Tcs'i null — bloklamayan modelde
        // kararı vermeyen her pod böyledir.
        reader.Get(req.Id).Should().NotBeNull("kayıt pub/sub ile diğer pod'a ulaşmalı");

        (await decider.DecideAsync(
            req.Id, approved: true, decidedBy: "admin", reason: "uygun",
            ct: TestContext.Current.CancellationToken)).Should().BeTrue();

        notified.Should().NotBeNull(
            "RequestDecided kararı vermeyen pod'da da tetiklenmeli — müşterinin SSE bağlantısı " +
            "o pod'da olabilir; tetiklenmezse bildirim hiç gitmez");
        notified!.Status.Should().Be(ApprovalStatus.Approved);
        notified.ExecutionResult.Should().Be("iade islendi");

        reader.Get(req.Id)!.Status.Should().Be(ApprovalStatus.Approved,
            "kararın kendisi de yayılmalı; aksi hâlde bu pod kaydı sonsuza kadar Pending sanır");
    }

    // ─── 2. Bayat cache mükerrer yürütmeye yol açmamalı ──────────────────────

    [Fact]
    public async Task Decide_OnSecondPodWithStaleCache_DoesNotExecuteTwice()
    {
        // İki AYRI hub: pod'lar aynı DB'yi paylaşır ama birbirinin mesajını almaz.
        // Bu, üretimde kaybolan/gecikmiş bir Redis mesajının karşılığıdır — cache bayat kalır.
        var routerA = new CountingExecutionRouter();
        var routerB = new CountingExecutionRouter();

        // Lock PAYLAŞILIR (üretimdeki tek Redis lock servisi gibi). Kilit, A'nın kararı
        // bittiğinde serbest kaldığı için B'yi durdurmaz — mükerrer yürütmeyi engelleyen
        // şeyin kilit DEĞİL, DB'deki koşullu sahiplenme olduğunu gösterir.
        var sharedLock = NewLock();

        var hubA = new InMemoryMessageBusHub();
        var hubB = new InMemoryMessageBusHub();

        var podA = NewQueue(hubA.CreateNode(), routerA, sharedLock);
        var podB = NewQueue(hubB.CreateNode(), routerB, sharedLock);

        var sessionId = $"sess-{Guid.NewGuid():N}";
        var req = NewRequest(sessionId);
        await podA.CreateAsync(req, TestContext.Current.CancellationToken);

        // Pod B kaydı DB'den (hydration) Pending olarak okur.
        podB.GetPending().Should().Contain(r => r.Id == req.Id);

        (await podA.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken))
            .Should().BeTrue();
        routerA.Count.Should().Be(1);

        // Pod B'nin belleği hâlâ "Pending" diyor. Admin'in ikinci isteği (ya da sweep) buraya düşerse:
        (await podB.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken))
            .Should().BeFalse("karar zaten verilmiş; DB'deki koşullu UPDATE 0 satır etkiler");

        routerB.Count.Should().Be(0,
            "iade/iptal ikinci kez YÜRÜTÜLMEMELİ — bu, müşteriye ikinci kez para iadesi demektir");
    }

    // ─── 3. Bayatlık fark edildiğinde kendini onarma ─────────────────────────

    [Fact]
    public async Task Decide_WhenAlreadyDecidedElsewhere_RefreshesItsOwnCacheFromDb()
    {
        var sharedLock = NewLock();
        var podA = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), sharedLock);
        var podB = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), sharedLock);

        var sessionId = $"sess-{Guid.NewGuid():N}";
        var req = NewRequest(sessionId);
        await podA.CreateAsync(req, TestContext.Current.CancellationToken);
        podB.GetPending().Should().Contain(r => r.Id == req.Id);

        await podA.DecideAsync(req.Id, false, "admin", "uygun degil", TestContext.Current.CancellationToken);

        // Başarısız sahiplenme, pod B'nin bayat olduğunu kesin olarak bildiği andır.
        await podB.DecideAsync(req.Id, true, "admin2", "onay", TestContext.Current.CancellationToken);

        podB.Get(req.Id)!.Status.Should().Be(ApprovalStatus.Rejected,
            "pod B kendini DB'den onarmalı; aksi hâlde 72 saat sonra sweep bu kaydı tekrar işlemeye çalışır");
        (await podB.GetUnseenForSessionAsync(sessionId, "ALFKI", TestContext.Current.CancellationToken))
            .Should().ContainSingle(r => r.Id == req.Id);
    }

    // ─── 4. Restart bekleyen onayları öldürmemeli ────────────────────────────

    [Fact]
    public async Task StartupRecovery_LeavesPendingApprovalsAlone_SoADeployDoesNotRejectThem()
    {
        // Bloklamayan modelde bekleyen bir onayın sahibi YOKTUR ve olmaması normaldir; admin
        // günler sonra karar verebilir (ApprovalOptions.StalePendingHours = 72). Startup'ta
        // 10 saniyeden eski Pending'leri Expired'a çeken eski davranış, her deploy'da bekleyen
        // tüm onayları sessizce reddediyordu.
        var queue = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());
        var req = NewRequest($"sess-{Guid.NewGuid():N}");
        await queue.CreateAsync(req, TestContext.Current.CancellationToken);

        // Kaydı 10 saniyelik eski eşiğin ötesine taşı — eski kod bunu tam olarak hedefliyordu.
        await using (var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var row = await ctx.Approvals.FirstAsync(a => a.Id == req.Id, TestContext.Current.CancellationToken);
            row.RequestedAt = DateTime.UtcNow.AddHours(-1);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var services = new ServiceCollection()
            .AddSingleton<IApprovalQueue>(queue)
            .BuildServiceProvider();

        var hydrator = new EfCore.PersistenceHydrator(services, NullLogger<EfCore.PersistenceHydrator>.Instance);
        await hydrator.StartAsync(TestContext.Current.CancellationToken);

        await using var verify = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var after = await verify.Approvals.AsNoTracking()
            .FirstAsync(a => a.Id == req.Id, TestContext.Current.CancellationToken);

        after.Status.Should().Be(nameof(ApprovalStatus.Pending),
            "restart, admin'in henüz karar vermediği bir talebi reddedemez");
    }

    // ─── 5. Kalıcı okuma Redis'e bağlı olmamalı ──────────────────────────────

    [Fact]
    public async Task UnseenAndHistory_AreVisible_EvenWhenTheDecisionMessageNeverArrives()
    {
        // İki AYRI hub — pod B kararı hiç duymaz. Redis yayını en-fazla-bir-kez teslimattır
        // ve hataları yutulur, yani bu üretimde gerçekleşebilecek bir durumdur. "Anlık bildirim
        // gitmese bile müşteri sonradan görür" güvencesi buna DAYANMAMALIDIR.
        var podA = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());
        var podB = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());

        var sessionId = $"sess-{Guid.NewGuid():N}";
        var req = NewRequest(sessionId);
        await podA.CreateAsync(req, TestContext.Current.CancellationToken);

        // Pod B'yi kararDAN ÖNCE hydrate et: cache'i "Pending" olarak donar ve bir daha DB okumaz.
        podB.GetPending().Should().Contain(r => r.Id == req.Id);

        await podA.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken);

        (await podB.GetUnseenForSessionAsync(sessionId, "ALFKI", TestContext.Current.CancellationToken))
            .Should().ContainSingle(r => r.Id == req.Id && r.Status == ApprovalStatus.Approved,
                "müşteri sonradan girdiğinde sonucu görmeli — bu sorgu kalıcı depodan cevaplanmalı");

        (await podB.GetHistoryForCustomerAsync("ALFKI", ct: TestContext.Current.CancellationToken))
            .Should().Contain(r => r.Id == req.Id && r.Status == ApprovalStatus.Approved);
    }

    // ─── 6. "Gördüm" işareti kararı geri alamamalı ───────────────────────────

    [Fact]
    public async Task MarkSeen_FromAPodWithStaleCache_DoesNotRevertTheDecision()
    {
        // Gerçekçi senaryo: istemci unseen listesini pod A'dan alır, "gördüm" isteği (ayrı bir
        // HTTP çağrısı) load balancer üzerinden pod B'ye düşer. Pod B kararı kaçırmıştır.
        var routerA = new CountingExecutionRouter();
        var routerB = new CountingExecutionRouter();
        var sharedLock = NewLock();

        var podA = NewQueue(new InMemoryMessageBusHub().CreateNode(), routerA, sharedLock);
        var podB = NewQueue(new InMemoryMessageBusHub().CreateNode(), routerB, sharedLock);

        var sessionId = $"sess-{Guid.NewGuid():N}";
        var req = NewRequest(sessionId);
        await podA.CreateAsync(req, TestContext.Current.CancellationToken);
        podB.GetPending().Should().Contain(r => r.Id == req.Id, "pod B cache'i Pending olarak dondu");

        await podA.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken);
        routerA.Count.Should().Be(1);

        await podB.MarkSeenAsync(req.Id, TestContext.Current.CancellationToken);

        await using var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var row = await ctx.Approvals.AsNoTracking()
            .FirstAsync(a => a.Id == req.Id, TestContext.Current.CancellationToken);

        row.Status.Should().Be(nameof(ApprovalStatus.Approved),
            "'gördüm' işareti yalnızca kendi kolonunu yazmalı; kaydı Pending'e geri çevirmesi " +
            "sadece yanlış etiket değil, kaydı yeniden sahiplenilebilir yapıp mükerrer yürütmeye açar");
        row.ExecutionResult.Should().Be("iade islendi", "yürütme sonucu silinmemeli");
        row.CustomerSeenAt.Should().NotBeNull();

        // Asıl tehlike: Pending'e dönmüş bir kayıt ikinci kez yürütülebilirdi.
        await podB.DecideAsync(req.Id, true, "admin", "tekrar", TestContext.Current.CancellationToken);
        routerB.Count.Should().Be(0);
    }

    // ─── 7. Eski bir Pending, cache sınırının gerisinde kaybolmamalı ─────────

    [Fact]
    public async Task Hydration_LoadsEveryPendingApproval_NotJustTheMostRecentPage()
    {
        // Yoğun bir kurulumda karara bağlanmış kayıtlar birikir. Cache yalnızca "son N kayıt"
        // çekseydi, onların gerisinde kalan eski bir Pending onay admin panelinde görünmez ve
        // StaleApprovalSweepService onu hiç bulamazdı — sonsuza kadar askıda kalırdı.
        var seeder = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());
        var sessionId = $"sess-{Guid.NewGuid():N}";

        var oldPending = NewRequest(sessionId);
        await seeder.CreateAsync(oldPending, TestContext.Current.CancellationToken);

        // Bu Pending'i, cache'in çektiği sayfanın (son 200 karar) gerisine düşür.
        await using (var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var row = await ctx.Approvals.FirstAsync(a => a.Id == oldPending.Id, TestContext.Current.CancellationToken);
            row.RequestedAt = DateTime.UtcNow.AddDays(-30);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

            var filler = Enumerable.Range(0, 250).Select(i => new ApprovalRequestEntity
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                SessionId = sessionId,
                CustomerId = "ALFKI",
                ToolName = WellKnown.ToolNames.OrderCancel,
                ParametersJson = "{}",
                RequestedAt = DateTime.UtcNow.AddMinutes(-i),
                DecidedAt = DateTime.UtcNow.AddMinutes(-i),
                Status = nameof(ApprovalStatus.Rejected),
                TimeoutSeconds = 60
            });
            ctx.Approvals.AddRange(filler);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Taze bir pod (restart) — cache'ini sıfırdan doldurur.
        var freshPod = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());

        freshPod.GetPending().Should().Contain(r => r.Id == oldPending.Id,
            "bekleyen bir onay, kaç yeni karar verilmiş olursa olsun görünür kalmalı");
    }

    // ─── 8. İnsan kararı ile yürütme sonucu ayrı izlenmeli ───────────────────

    /// <summary>Yürütme SIRASINDA DB'nin ne gördüğünü yakalayan router.</summary>
    private sealed class DbObservingRouter(
        Microsoft.EntityFrameworkCore.IDbContextFactory<EfCore.CustomerSupportDbContext> dbFactory,
        bool succeeds) : IApprovalExecutionRouter
    {
        public string? StatusSeenMidExecution { get; private set; }
        public string? ExecStatusSeenMidExecution { get; private set; }

        public async Task<ApprovalExecutionOutcome> ExecuteAsync(ApprovalRequest request, CancellationToken ct = default)
        {
            await using var ctx = await dbFactory.CreateDbContextAsync(ct);
            var row = await ctx.Approvals.AsNoTracking().FirstAsync(a => a.Id == request.Id, ct);
            StatusSeenMidExecution = row.Status;
            ExecStatusSeenMidExecution = row.ExecutionStatus;
            return new ApprovalExecutionOutcome(succeeds, succeeds ? "iade islendi" : "siparis zaten iptal edilmis");
        }
    }

    [Fact]
    public async Task Decide_MarksExecutionRunningBeforeTheWorkStarts_SoACrashLeavesAVisibleRecord()
    {
        // Bulgu: karar yazıldıktan sonra, sonuç yazılmadan önce süreç kapanırsa (deploy/crash)
        // gerçek iş çalışmamış olabilir. Eskiden bu pencere DB'de hiçbir iz bırakmıyordu —
        // kayıt sadece "Approved" görünür, kimse askıda olduğunu anlayamazdı. Artık aynı anda
        // ExecutionStatus=Running yazılıyor, yani crash olsa bile kayıt "askıda" olarak kalıyor.
        var router = new DbObservingRouter(_fixture.DbFactory, succeeds: true);
        var queue = NewQueue(new InMemoryMessageBusHub().CreateNode(), router, NewLock());

        var req = NewRequest($"sess-{Guid.NewGuid():N}");
        await queue.CreateAsync(req, TestContext.Current.CancellationToken);
        await queue.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken);

        router.StatusSeenMidExecution.Should().Be(nameof(ApprovalStatus.Approved));
        router.ExecStatusSeenMidExecution.Should().Be(nameof(ApprovalExecutionStatus.Running),
            "iş çalışırken kayıt DB'de 'askıda' görünmeli; tam bu anda süreç ölürse geriye " +
            "kalan tek iz budur");

        await using var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var final = await ctx.Approvals.AsNoTracking()
            .FirstAsync(a => a.Id == req.Id, TestContext.Current.CancellationToken);
        final.ExecutionStatus.Should().Be(nameof(ApprovalExecutionStatus.Succeeded));
    }

    [Fact]
    public async Task Decide_WhenTheToolItselfFails_RecordsFailedExecution_NotAPlainApproval()
    {
        // Bulgu: ApprovalExecutionOutcome.Success üretiliyor ama atılıyordu. Tool kendi işini
        // reddetse bile (ör. "sipariş zaten iptal edilmiş") kayıt yalnızca "Onaylandı" görünüyordu.
        var router = new DbObservingRouter(_fixture.DbFactory, succeeds: false);
        var queue = NewQueue(new InMemoryMessageBusHub().CreateNode(), router, NewLock());

        var req = NewRequest($"sess-{Guid.NewGuid():N}");
        await queue.CreateAsync(req, TestContext.Current.CancellationToken);
        await queue.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken);

        await using var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var row = await ctx.Approvals.AsNoTracking()
            .FirstAsync(a => a.Id == req.Id, TestContext.Current.CancellationToken);

        row.Status.Should().Be(nameof(ApprovalStatus.Approved), "insanın kararı gerçekten onaydı");
        row.ExecutionStatus.Should().Be(nameof(ApprovalExecutionStatus.Failed),
            "ama iş başarısız oldu; ikisi ayrı alanlar olmalı ki panel 'sorunsuz' göstermesin");
        row.ExecutionResult.Should().Be("siparis zaten iptal edilmis");
    }

    [Fact]
    public async Task Decide_Reject_LeavesExecutionStatusAsNone()
    {
        var router = new CountingExecutionRouter();
        var queue = NewQueue(new InMemoryMessageBusHub().CreateNode(), router, NewLock());

        var req = NewRequest($"sess-{Guid.NewGuid():N}");
        await queue.CreateAsync(req, TestContext.Current.CancellationToken);
        await queue.DecideAsync(req.Id, false, "admin", "uygun degil", TestContext.Current.CancellationToken);

        await using var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var row = await ctx.Approvals.AsNoTracking()
            .FirstAsync(a => a.Id == req.Id, TestContext.Current.CancellationToken);

        row.ExecutionStatus.Should().Be(nameof(ApprovalExecutionStatus.None), "reddedilen talepte yürütülecek iş yok");
        router.Count.Should().Be(0);
    }

    // ─── 9. Yürütme sonucu cross-pod yayılmalı ──────────────────────────────

    [Fact]
    public async Task Decide_PropagatesExecutionStatus_SoAFailedRunIsNotShownAsPlainApproval()
    {
        var hub = new InMemoryMessageBusHub();
        var decider = NewQueue(hub.CreateNode(), new DbObservingRouter(_fixture.DbFactory, succeeds: false), NewLock());

        // Okuyucu pod'un DB'si kapalı: gördüğü her şey pub/sub'dan gelmiş olmalı.
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = NewQueue(hub.CreateNode(), new CountingExecutionRouter(), NewLock(), neverReachesDb);

        ApprovalRequest? notified = null;
        reader.RequestDecided += (_, r) => notified = r;

        var req = NewRequest($"sess-{Guid.NewGuid():N}");
        await decider.CreateAsync(req, TestContext.Current.CancellationToken);
        await decider.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken);

        notified.Should().NotBeNull();
        notified!.ExecutionStatus.Should().Be(ApprovalExecutionStatus.Failed,
            "yürütme durumu Redis payload'ında taşınmalı; taşınmazsa alıcı pod None'a düşer ve " +
            "başarısız bir işlem müşteriye anlık olarak düz 'Onaylandı' görünür");
    }

    [Fact]
    public async Task RemoteDecision_IsApplied_EvenIfThePodHydratedMidExecution()
    {
        // Bir pod kaydı tam yürütme sırasında hydrate ederse belleğinde Approved+Running olur.
        // "Status != Pending ise atla" ölçütü, yürütmenin BİTTİĞİNİ bildiren mesajı da atlardı.
        var hub = new InMemoryMessageBusHub();
        var decider = NewQueue(hub.CreateNode(), new CountingExecutionRouter(), NewLock());
        var reader = NewQueue(hub.CreateNode(), new CountingExecutionRouter(), NewLock());

        var req = NewRequest($"sess-{Guid.NewGuid():N}");
        await decider.CreateAsync(req, TestContext.Current.CancellationToken);

        // reader'ın belleğini "yürütme sırasındaki" hâle getir.
        var entry = reader.Get(req.Id);
        entry.Should().NotBeNull();
        entry!.Status = ApprovalStatus.Approved;
        entry.ExecutionStatus = ApprovalExecutionStatus.Running;

        ApprovalRequest? notified = null;
        reader.RequestDecided += (_, r) => notified = r;

        await decider.DecideAsync(req.Id, true, "admin", "uygun", TestContext.Current.CancellationToken);

        notified.Should().NotBeNull("yürütme sonucu henüz bilinmiyordu; mesaj atlanmamalı");
        reader.Get(req.Id)!.ExecutionStatus.Should().Be(ApprovalExecutionStatus.Succeeded);
    }

    // ─── 10. Eski bir unseen kayıt "görüldü" işaretlenebilmeli ──────────────

    [Fact]
    public async Task GetAsync_FindsRecordsThatFellOutOfTheCache_SoTheyCanBeMarkedSeen()
    {
        // unseen listesi kalıcı depodan cevaplanıyor; sahiplik kontrolü cache'e bakarsa
        // listelenen eski bir bildirim için 404 döner ve bildirim her girişte tekrar görünür.
        var seeder = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());
        var sessionId = $"sess-{Guid.NewGuid():N}";

        var oldDecided = NewRequest(sessionId);
        await seeder.CreateAsync(oldDecided, TestContext.Current.CancellationToken);
        await seeder.DecideAsync(oldDecided.Id, false, "admin", "uygun degil", TestContext.Current.CancellationToken);

        // Kaydı cache penceresinin (son 200 karar) gerisine düşür.
        await using (var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var row = await ctx.Approvals.FirstAsync(a => a.Id == oldDecided.Id, TestContext.Current.CancellationToken);
            row.RequestedAt = DateTime.UtcNow.AddDays(-60);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

            ctx.Approvals.AddRange(Enumerable.Range(0, 250).Select(i => new ApprovalRequestEntity
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                SessionId = sessionId,
                CustomerId = "ALFKI",
                ToolName = WellKnown.ToolNames.OrderCancel,
                ParametersJson = "{}",
                RequestedAt = DateTime.UtcNow.AddMinutes(-i),
                DecidedAt = DateTime.UtcNow.AddMinutes(-i),
                Status = nameof(ApprovalStatus.Rejected),
                ExecutionStatus = nameof(ApprovalExecutionStatus.None),
                CustomerSeenAt = DateTime.UtcNow,
                TimeoutSeconds = 60
            }));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var freshPod = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());

        (await freshPod.GetUnseenForSessionAsync(sessionId, "ALFKI", TestContext.Current.CancellationToken))
            .Should().Contain(r => r.Id == oldDecided.Id, "unseen kalıcı depodan cevaplanıyor");

        freshPod.Get(oldDecided.Id).Should().BeNull("kayıt cache penceresinin dışında — sorunun kaynağı bu");

        (await freshPod.GetAsync(oldDecided.Id, TestContext.Current.CancellationToken))
            .Should().NotBeNull("listelenen bir kayıt için 'gördüm' isteği 404 dönmemeli");
    }

    // ─── 11. Askıda kalan yürütme, listeden düşse bile görünmeli ────────────

    [Fact]
    public async Task GetStuckExecutions_FindsOldRunningRecords_ThatRecentListsWouldHide()
    {
        var seeder = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());
        var stuckId = Guid.NewGuid().ToString("N")[..12];

        await using (var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            // Yürütme sırasında süreci ölmüş bir kayıt: Approved + Running, sonuç yazılmamış.
            ctx.Approvals.Add(new ApprovalRequestEntity
            {
                Id = stuckId,
                SessionId = $"sess-{Guid.NewGuid():N}",
                CustomerId = "ALFKI",
                ToolName = WellKnown.ToolNames.ReturnRequest,
                ParametersJson = "{}",
                RequestedAt = DateTime.UtcNow.AddDays(-45),
                DecidedAt = DateTime.UtcNow.AddDays(-45),
                Status = nameof(ApprovalStatus.Approved),
                ExecutionStatus = nameof(ApprovalExecutionStatus.Running),
                TimeoutSeconds = 60
            });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var stuck = await seeder.GetStuckExecutionsAsync(TestContext.Current.CancellationToken);

        stuck.Should().Contain(r => r.Id == stuckId,
            "elle müdahale bekleyen kayıt, kaç yeni karar verilmiş olursa olsun görünmeli — " +
            "'son N kayıt' listeleri bu iş için yetersizdir");
    }

    // ─── 12. Taze bir yürütme "askıda" sayılmamalı ──────────────────────────

    [Fact]
    public async Task GetStuckExecutions_IgnoresRunsThatAreStillYoungEnoughToBeInProgress()
    {
        // Panel bu listeyi "deploy/crash oldu, elle doğrulayın" diye gösteriyor ve 15 saniyede
        // bir yeniliyor. Yaş eşiği olmasaydı, normal ama uzun süren her yürütme askıda görünür
        // ve uyarı hızla anlamsızlaşırdı.
        var queue = NewQueue(new InMemoryMessageBusHub().CreateNode(), new CountingExecutionRouter(), NewLock());

        var freshId = Guid.NewGuid().ToString("N")[..12];
        var oldId = Guid.NewGuid().ToString("N")[..12];

        await using (var ctx = await _fixture.DbFactory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            ctx.Approvals.AddRange(
                Running(freshId, DateTime.UtcNow.AddMinutes(-1)),    // henüz çalışıyor olabilir
                Running(oldId, DateTime.UtcNow.AddHours(-3)));       // bu gerçekten askıda
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var stuck = await queue.GetStuckExecutionsAsync(TestContext.Current.CancellationToken);

        stuck.Should().Contain(r => r.Id == oldId);
        stuck.Should().NotContain(r => r.Id == freshId,
            "1 dakikalık bir yürütme hâlâ devam ediyor olabilir; onu crash diye göstermek yanlış alarmdır");

        static ApprovalRequestEntity Running(string id, DateTime decidedAt) => new()
        {
            Id = id,
            SessionId = $"sess-{Guid.NewGuid():N}",
            CustomerId = "ALFKI",
            ToolName = WellKnown.ToolNames.ReturnRequest,
            ParametersJson = "{}",
            RequestedAt = decidedAt,
            DecidedAt = decidedAt,
            Status = nameof(ApprovalStatus.Approved),
            ExecutionStatus = nameof(ApprovalExecutionStatus.Running),
            TimeoutSeconds = 60
        };
    }
}
