// Öz-eleştiri ve bağlam kullanımı KALICI olmalı. Canlı veride 56 izin hiçbirinde öz-eleştiri yoktu: alan yalnızca bellek
// önbelleğindeki nesnede yaşıyor, veritabanına hiç yazılmıyordu — yeniden başlatma ya da başka pod'da LessonMiner'ın
// "öz-eleştiri sorun işaret ediyorsa ders çıkar" sinyali kayboluyordu (üstelik Response prompt'u modele "bu gerçekten okunuyor" diyor).

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresReasoningTraceCritiqueTests(PostgresCatalogFixture fixture)
{
    private PostgresReasoningTraceStore NewStore() =>
        new(fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresReasoningTraceStore>.Instance);

    [Fact]
    public async Task TheSelfCritique_SurvivesARestart_AndKeepsItsConcernFlag()
    {
        var store = NewStore();
        var trace = await store.StartTraceAsync("oturum", "siparişim nerede");
        trace.SelfCritique = new SelfCritique
        {
            AddressesUserQuery = true, Tone = WellKnown.CritiqueTones.Robotic, Completeness = 0.6, HallucinationRisk = 0.7,
            Sources = ["OrderAgent.resultNotes"], IssuesFound = ["kesin tarih verdi"], RevisionNeeded = true, RevisionNotes = "tarihi çıkar"
        };
        store.Update(trace);
        await store.CompleteAsync(trace.TraceId, "completed", "kargoda");

        var fromDb = NewStore().Get(trace.TraceId)!;   // boş önbellek → doğrudan veritabanı

        fromDb.SelfCritique.Should().NotBeNull();
        fromDb.SelfCritique!.HallucinationRisk.Should().Be(0.7);
        fromDb.SelfCritique.IssuesFound.Should().Equal("kesin tarih verdi");
        fromDb.SelfCritique.RevisionNeeded.Should().BeTrue();
        fromDb.SelfCritique.IsConcerning.Should().BeTrue();
    }

    [Fact]
    public async Task TheContextUsage_IsPersisted_SoAnAdminCanSeeWhatTheModelKnew()
    {
        var store = NewStore();
        var trace = await store.StartTraceAsync("oturum", "merhaba");
        trace.ContextParts =
        [
            new ContextPartUsage { ProviderName = "CustomerProfile", Status = "included", Length = 321 },
            new ContextPartUsage { ProviderName = "SemanticMemory", Status = "timedOut", Length = 0 }
        ];
        store.Update(trace);
        await store.CompleteAsync(trace.TraceId, "completed", "selam");

        var fromDb = NewStore().Get(trace.TraceId)!;

        fromDb.ContextParts.Should().HaveCount(2);
        fromDb.ContextParts[1].Status.Should().Be("timedOut");
        fromDb.ContextParts[0].Length.Should().Be(321);
    }

    [Fact]
    public async Task ATraceWithoutACritique_ReadsBackWithNone()
    {
        var store = NewStore();
        var trace = await store.StartTraceAsync("oturum", "merhaba");
        await store.CompleteAsync(trace.TraceId, "completed", "selam");

        var fromDb = NewStore().Get(trace.TraceId)!;

        fromDb.SelfCritique.Should().BeNull();
        fromDb.ContextParts.Should().BeEmpty();
    }

    [Fact]
    public async Task TheSelfCritique_IsAlsoWhatTheRehydratedCacheReturns()
    {
        var store = NewStore();
        var trace = await store.StartTraceAsync("oturum-isitma", "q");
        trace.SelfCritique = new SelfCritique { HallucinationRisk = 0.9 };
        store.Update(trace);
        await store.CompleteAsync(trace.TraceId, "completed", "a");

        var rehydrated = NewStore();
        await rehydrated.WarmUpAsync(TestContext.Current.CancellationToken);

        rehydrated.GetRecent(500).Single(t => t.TraceId == trace.TraceId).SelfCritique!.HallucinationRisk.Should().Be(0.9);
    }
}
