// Tests/Services/LessonMinerApprovalTests.cs
//
// LessonMiner.ApproveAsync'in durum geçişleri.
//
// Önemli olan köşe: vektör hafızaya yazım başarısız olursa ApproveAsync hatayı YUTAR ve dersi
// yine de Approved işaretler (bkz. metottaki catch). Bu durumda ders DB'de "onaylı" görünür
// ama hiçbir konuşmaya context olarak girmez — yani onay pratikte etkisizdir. Ayırt edici
// sinyal VectorMemoryId'nin boş kalmasıdır; admin paneli bu durumu uyarı + "yeniden yaz"
// butonu olarak gösterir ve aynı approve ucunu tekrar çağırır.
//
// Bu testler yeniden-deneme yolunun açık, normal çift-onayın ise kapalı kaldığını kilitler.

using CustomerSupportBot.Application.Ports.Outbound;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Application.Ports.Outbound.Persistence;
using CustomerSupportBot.Application.Services.Improvement;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Improvement;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Application.Ports.Outbound.Observability;

namespace CustomerSupportBot.Application.Tests;

public class LessonMinerApprovalTests
{
    private sealed class FakeLessonStore : ILessonStore
    {
        private readonly Dictionary<string, Lesson> _items = new(StringComparer.Ordinal);
        public void Add(Lesson lesson) => _items[lesson.Id] = lesson;
        public Lesson? Get(string id) => _items.GetValueOrDefault(id);
        public IReadOnlyList<Lesson> GetByStatus(LessonStatus status) =>
            _items.Values.Where(l => l.Status == status).ToList();
        public IReadOnlyList<Lesson> GetAll(int limit = 200) => _items.Values.Take(limit).ToList();
        public void Update(Lesson lesson) => _items[lesson.Id] = lesson;
        public int UpdateCount { get; private set; }
    }

    private static (LessonMiner Miner, FakeLessonStore Store) Build()
    {
        var store = new FakeLessonStore();
        var miner = new LessonMiner(
            Substitute.For<IReasoningTraceStore>(),
            Substitute.For<IRatingStore>(),
            store,
            Substitute.For<IGeneralChatClient>(),
            Options.Create(new SelfImprovementOptions { Enabled = true }),
            NullLogger<LessonMiner>.Instance,
            memory: null);          // hafıza yok → VectorMemoryId hep boş kalır
        return (miner, store);
    }

    private static Lesson Seed(FakeLessonStore store, LessonStatus status, string? vectorId = null)
    {
        var lesson = new Lesson
        {
            Title = "Test dersi",
            LessonText = "metin",
            Status = status,
            VectorMemoryId = vectorId
        };
        store.Add(lesson);
        return lesson;
    }

    [Fact]
    public async Task ApproveAsync_ProposedLesson_BecomesApproved()
    {
        var (miner, store) = Build();
        var lesson = Seed(store, LessonStatus.Proposed);

        var ok = await miner.ApproveAsync(lesson.Id, "admin", "gerekçe", TestContext.Current.CancellationToken);

        ok.Should().BeTrue();
        store.Get(lesson.Id)!.Status.Should().Be(LessonStatus.Approved);
        store.Get(lesson.Id)!.DecisionReason.Should().Be("gerekçe");
    }

    [Fact]
    public async Task ApproveAsync_ApprovedButNoVectorId_AllowsRetry()
    {
        // "Onaylı ama etkisiz" ders — yeniden yazım denemesi kabul edilmeli.
        var (miner, store) = Build();
        var lesson = Seed(store, LessonStatus.Approved, vectorId: null);

        var ok = await miner.ApproveAsync(lesson.Id, "admin", reason: null, TestContext.Current.CancellationToken);

        ok.Should().BeTrue("vektör yazımı başarısız kalmış bir onay yeniden denenebilmeli");
    }

    [Fact]
    public async Task ApproveAsync_Retry_PreservesOriginalDecisionReason()
    {
        // Yeniden deneme bir "yeni karar" değil — ilk onaydaki gerekçe korunmalı.
        var (miner, store) = Build();
        var lesson = Seed(store, LessonStatus.Approved, vectorId: null);
        lesson.DecisionReason = "ilk onay gerekçesi";
        store.Update(lesson);

        await miner.ApproveAsync(lesson.Id, "admin2", reason: null, TestContext.Current.CancellationToken);

        store.Get(lesson.Id)!.DecisionReason.Should().Be("ilk onay gerekçesi");
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApprovedWithVectorId_IsRejected()
    {
        // Hafızaya yazılmış ders zaten etkin — tekrar onaylanmamalı (mükerrer yazım önlenir).
        var (miner, store) = Build();
        var lesson = Seed(store, LessonStatus.Approved, vectorId: "vec-1");

        var ok = await miner.ApproveAsync(lesson.Id, "admin", "tekrar", TestContext.Current.CancellationToken);

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task ApproveAsync_RejectedLesson_IsNotResurrected()
    {
        var (miner, store) = Build();
        var lesson = Seed(store, LessonStatus.Rejected);

        var ok = await miner.ApproveAsync(lesson.Id, "admin", "fikrimi değiştirdim", TestContext.Current.CancellationToken);

        ok.Should().BeFalse();
        store.Get(lesson.Id)!.Status.Should().Be(LessonStatus.Rejected);
    }

    [Fact]
    public async Task ApproveAsync_UnknownId_ReturnsFalse()
    {
        var (miner, _) = Build();
        (await miner.ApproveAsync("yok-boyle-bir-id", "admin", null, TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    // ─── suggestedAgent doğrulaması ────────────────────────────────────────────────
    // LLM'in verdiği ajan adı panelde rozet olarak gösteriliyor; uydurma bir ad
    // "bu ders şu ajanı ilgilendiriyor" izlenimi verir. Tanınmayan değer null'a düşer.

    [Theory]
    [InlineData("PlanningAgent", "PlanningAgent")]
    [InlineData("planningagent", "PlanningAgent")]   // büyük/küçük harf duyarsız, kanonik döner
    [InlineData("  OrderAgent ", "OrderAgent")]      // kırpılır
    public void NormalizeAgent_KnownAgent_ReturnsCanonicalName(string raw, string expected)
        => LessonMiner.NormalizeAgent(raw).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData("SupportAgent")]      // var olmayan ajan
    [InlineData("OrderAgentX")]       // yakın ama yanlış
    public void NormalizeAgent_UnknownOrEmpty_ReturnsNull(string? raw)
        => LessonMiner.NormalizeAgent(raw).Should().BeNull();

    [Fact]
    public void NormalizeAgent_AcceptsEveryRealAgentName()
    {
        // WellKnown'a yeni ajan eklenirse bu test onun da kabul edildiğini garanti eder.
        foreach (var agent in WellKnown.AgentNames.All)
            LessonMiner.NormalizeAgent(agent).Should().Be(agent);
    }
}
