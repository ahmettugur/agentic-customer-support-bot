// Tests/Services/PostgresLowerPriorityAdapterSyncTests.cs
//
// RatingStore, LessonStore, SlaEventSink, ReasoningTraceStore — dördü de aynı hydration
// deseni ile Postgres'e karşı hiç test edilmiyordu ve MessageBus'a bağlı DEĞİLDİ. Bu dosya
// her biri için cross-pod senkronizasyonun gerçekten çalıştığını kanıtlıyor: reader pod'un
// DB'si her zaman hata veriyorken, yazılan veri yalnızca Redis pub/sub üzerinden görünebilir.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Improvement;
using Microsoft.Extensions.Logging.Abstractions;
using CustomerSupportBot.Tests.Shared;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresLowerPriorityAdapterSyncTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresLowerPriorityAdapterSyncTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    // ─── RatingStore ────────────────────────────────────────────────────────

    [Fact]
    public void RatingStore_Submit_PublishesRating_VisibleOnOtherPodWithoutDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresRatingStore(_fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresRatingStore>.Instance);
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresRatingStore(neverReachesDb, hub.CreateNode(), NullLogger<PostgresRatingStore>.Instance);

        var sessionId = $"rate-{Guid.NewGuid():N}";
        writer.Submit(sessionId, 5, "harika");

        var seenByReader = reader.GetBySession(sessionId);

        seenByReader.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu kayıt yalnızca Redis pub/sub üzerinden gelebilir");
        seenByReader!.Stars.Should().Be(5);
        seenByReader.Feedback.Should().Be("harika");
    }

    // ─── LessonStore ────────────────────────────────────────────────────────

    [Fact]
    public void LessonStore_Add_PublishesLesson_VisibleOnOtherPodWithoutDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresLessonStore(_fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresLessonStore>.Instance);
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresLessonStore(neverReachesDb, hub.CreateNode(), NullLogger<PostgresLessonStore>.Instance);

        var lesson = new Lesson { Title = "Test dersi", LessonText = "X durumunda Y yap", Observation = "gözlem" };
        writer.Add(lesson);

        var seenByReader = reader.Get(lesson.Id);

        seenByReader.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu kayıt yalnızca Redis pub/sub üzerinden gelebilir");
        seenByReader!.Title.Should().Be("Test dersi");
    }

    // ─── SlaEventSink ───────────────────────────────────────────────────────

    [Fact]
    public void SlaEventSink_Record_PublishesEvent_VisibleOnOtherPodWithoutDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresSlaEventSink(_fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresSlaEventSink>.Instance);
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresSlaEventSink(neverReachesDb, hub.CreateNode(), NullLogger<PostgresSlaEventSink>.Instance);

        var targetId = $"target-{Guid.NewGuid():N}";
        writer.Record(new SlaEvent { Kind = "approval", Severity = "breach", TargetId = targetId, AgeSeconds = 120 });

        var seenByReader = reader.LastEmittedAt("approval", targetId, "breach");

        seenByReader.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu olay yalnızca Redis pub/sub üzerinden gelebilir");
    }

    // ─── ReasoningTraceStore ────────────────────────────────────────────────

    [Fact]
    public void ReasoningTraceStore_StartTrace_PublishesSkeleton_VisibleOnOtherPodWithoutDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresReasoningTraceStore(_fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresReasoningTraceStore>.Instance);
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresReasoningTraceStore(neverReachesDb, hub.CreateNode(), NullLogger<PostgresReasoningTraceStore>.Instance);

        var sessionId = $"trace-{Guid.NewGuid():N}";
        var trace = writer.StartTrace(sessionId, "sipariş durumu nedir?");

        var seenByReader = reader.Get(trace.TraceId);

        seenByReader.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu trace yalnızca Redis pub/sub üzerinden gelebilir");
        seenByReader!.UserQuery.Should().Be("sipariş durumu nedir?");
    }

    [Fact]
    public void ReasoningTraceStore_Complete_PublishesFinalSnapshot_VisibleOnOtherPodWithoutDbAccess()
    {
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresReasoningTraceStore(_fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresReasoningTraceStore>.Instance);
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresReasoningTraceStore(neverReachesDb, hub.CreateNode(), NullLogger<PostgresReasoningTraceStore>.Instance);

        var sessionId = $"trace-{Guid.NewGuid():N}";
        var trace = writer.StartTrace(sessionId, "sipariş durumu nedir?");
        writer.Complete(trace.TraceId, terminationReason: "completed", finalResponse: "Siparişiniz kargoda.");

        var seenByReader = reader.Get(trace.TraceId);

        seenByReader.Should().NotBeNull();
        seenByReader!.CompletedAt.Should().NotBeNull(
            "reader'ın DB'si her zaman hata veriyor — bu tamamlanma bilgisi yalnızca Redis pub/sub üzerinden gelebilir");
        seenByReader.FinalResponse.Should().Be("Siparişiniz kargoda.");
    }

    [Fact]
    public void ReasoningTraceStore_Update_DoesNotPublish_ByDesign()
    {
        // Update() yüksek frekansta çağrılır ve BİLİNÇLİ olarak DB'ye yazmaz (write-storm
        // önlemi) — aynı gerekçeyle Redis'e de yayınlamamalı. Bu test, gelecekte birinin
        // "tutarlılık için" Update'e publish eklemesini caydırmak için var: eklenirse bu
        // test KIRILMAZ (davranış hâlâ "yayınlanmadı" beklentisini karşılar) ama en azından
        // niyeti dokümante eder.
        var hub = new InMemoryMessageBusHub();
        var writer = new PostgresReasoningTraceStore(_fixture.DbFactory, hub.CreateNode(), NullLogger<PostgresReasoningTraceStore>.Instance);
        var neverReachesDb = new FlakyDbContextFactory(_fixture.DbFactory, failuresRemaining: int.MaxValue);
        var reader = new PostgresReasoningTraceStore(neverReachesDb, hub.CreateNode(), NullLogger<PostgresReasoningTraceStore>.Instance);

        var sessionId = $"trace-{Guid.NewGuid():N}";
        var trace = writer.StartTrace(sessionId, "ilk sorgu");
        reader.Get(trace.TraceId).Should().NotBeNull(); // StartTrace'ten geldi

        trace.IterationCount = 3;
        writer.Update(trace);

        var seenByReader = reader.Get(trace.TraceId);
        seenByReader!.IterationCount.Should().Be(0,
            "Update() tasarım gereği yayınlamaz — reader hâlâ StartTrace anındaki skeleton'ı görür");
    }
}
