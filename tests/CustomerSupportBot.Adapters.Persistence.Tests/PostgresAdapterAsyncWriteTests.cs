// Postgres adaptörlerinin İŞLEM BAŞINA yazmaları çağıranı bloklıyor mu?
//
// Hibrit cache adaptörleri senkron port arayüzlerinin arkasında DB'ye
// GetAwaiter().GetResult() ile yazıyordu: eskalasyon oluşturma/karar, SLA olayı, ders,
// puan, müşteri profili, canlı devralma ve admin/sistem/bot mesajları. DB yavaşladığında her
// biri bir thread-pool thread'ini rehin tutuyordu. Ölçüm: NonBlocking.ReturnsWhileDbIsBlockedAsync.
//
// Her testte kapılı (gated) örnek kendi ilk DB bağlantısında bekler — bu, çoğu zaman lazy
// hydrate'tir. Yani ölçüm yazma yolunun hydrate DAHİL baştan sona asenkron olduğunu doğrular.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Model.Improvement;
using CustomerSupportBot.Domain.Model.Memory;
using CustomerSupportBot.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresAdapterAsyncWriteTests
{
    private readonly PostgresCatalogFixture _fixture;

    public PostgresAdapterAsyncWriteTests(PostgresCatalogFixture fixture) => _fixture = fixture;

    private GatedDbContextFactory Gate() => new(_fixture.DbFactory);

    private static string NewId(string tag) => $"{tag}-{Guid.NewGuid():N}";

    private const string Because = "DB yazması beklenirken çağıranın thread'i bloklanmamalı";

    // ─── SLA ───

    [Fact]
    public async Task SlaEventSink_RecordAsync()
    {
        var gate = Gate();
        var sink = new PostgresSlaEventSink(gate, new NoopMessageBus(), NullLogger<PostgresSlaEventSink>.Instance);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => sink.RecordAsync(
            new SlaEvent { Kind = "approval", Severity = "warn", TargetId = NewId("sla"), AgeSeconds = 30 })))
            .Should().BeTrue(Because);
    }

    // ─── Lesson ───

    [Fact]
    public async Task LessonStore_AddAsync()
    {
        var gate = Gate();
        var store = new PostgresLessonStore(gate, new NoopMessageBus(), NullLogger<PostgresLessonStore>.Instance);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => store.AddAsync(
            new Lesson { Title = "ders", LessonText = "X durumunda Y yap", Observation = "gözlem" })))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task LessonStore_UpdateAsync()
    {
        var lesson = new Lesson { Title = "ders", LessonText = "X durumunda Y yap", Observation = "gözlem" };
        await new PostgresLessonStore(_fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresLessonStore>.Instance)
            .AddAsync(lesson);

        var gate = Gate();
        var store = new PostgresLessonStore(gate, new NoopMessageBus(), NullLogger<PostgresLessonStore>.Instance);
        lesson.Status = LessonStatus.Rejected;

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => store.UpdateAsync(lesson)))
            .Should().BeTrue(Because);
    }

    // ─── Rating ───

    [Fact]
    public async Task RatingStore_SubmitAsync()
    {
        var gate = Gate();
        var store = new PostgresRatingStore(gate, new NoopMessageBus(), NullLogger<PostgresRatingStore>.Instance);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => store.SubmitAsync(NewId("rating"), 4, "iyi")))
            .Should().BeTrue(Because);
    }

    // ─── Customer profile ───

    private PostgresCustomerProfileStore ProfileStore(GatedDbContextFactory gate)
        => new(gate, new NoopMessageBus(), NullLogger<PostgresCustomerProfileStore>.Instance);

    [Fact]
    public async Task CustomerProfileStore_UpsertAsync()
    {
        var gate = Gate();
        var store = ProfileStore(gate);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => store.UpsertAsync(
            new CustomerProfile { CustomerId = NewId("cust") })))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task CustomerProfileStore_GetOrCreateAsync()
    {
        var gate = Gate();
        var store = ProfileStore(gate);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => store.GetOrCreateAsync(NewId("cust"))))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task CustomerProfileStore_DeleteAsync()
    {
        var gate = Gate();
        var store = ProfileStore(gate);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => store.DeleteAsync(NewId("cust"))))
            .Should().BeTrue(Because);
    }

    // ─── Escalation ───

    [Fact]
    public async Task EscalationSink_CreateAsync()
    {
        var gate = Gate();
        var sink = new PostgresEscalationSink(gate, new NoopMessageBus(), NullLogger<PostgresEscalationSink>.Instance);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => sink.CreateAsync(
            new EscalationRequest { SessionId = NewId("esc"), UserQuery = "temsilci", Reason = "test" })))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task EscalationSink_DecideAsync()
    {
        var created = await new PostgresEscalationSink(_fixture.DbFactory, new NoopMessageBus(), NullLogger<PostgresEscalationSink>.Instance)
            .CreateAsync(new EscalationRequest { SessionId = NewId("esc"), UserQuery = "temsilci", Reason = "test" });

        var gate = Gate();
        var sink = new PostgresEscalationSink(gate, new NoopMessageBus(), NullLogger<PostgresEscalationSink>.Instance);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => sink.DecideAsync(created.Id, WellKnown.EscalationActions.Acknowledge, "alice")))
            .Should().BeTrue(Because);
    }

    // ─── Canlı devralma ───

    private static PostgresChatModeRegistry ModeRegistry(IDbContextFactory<CustomerSupportDbContext> dbFactory) => new(
        dbFactory, new NoopMessageBus(),
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 })),
        NullLogger<PostgresChatModeRegistry>.Instance);

    [Fact]
    public async Task ChatModeRegistry_TakeOverAsync()
    {
        var gate = Gate();
        var registry = ModeRegistry(gate);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => registry.TakeOverAsync(NewId("mode"), "alice")))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task ChatModeRegistry_ReleaseAsync()
    {
        var sessionId = NewId("mode");
        await ModeRegistry(_fixture.DbFactory).TakeOverAsync(sessionId, "alice");

        var gate = Gate();
        var registry = ModeRegistry(gate);

        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => registry.ReleaseAsync(sessionId)))
            .Should().BeTrue(Because);
    }

    // ─── Chat bridge: temsilci/sistem/bot mesajları ───

    private static PostgresChatBridge Bridge(GatedDbContextFactory gate)
        => new(gate, new NoopMessageBus(), NullLogger<PostgresChatBridge>.Instance);

    [Fact]
    public async Task ChatBridge_PublishAdminMessageAsync()
    {
        var gate = Gate();
        var bridge = Bridge(gate);
        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => bridge.PublishAdminMessageAsync(NewId("br"), "alice", "merhaba")))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task ChatBridge_PublishSystemMessageAsync()
    {
        var gate = Gate();
        var bridge = Bridge(gate);
        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => bridge.PublishSystemMessageAsync(NewId("br"), "temsilci katıldı")))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task ChatBridge_PublishAdminOnlyMessageAsync()
    {
        var gate = Gate();
        var bridge = Bridge(gate);
        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => bridge.PublishAdminOnlyMessageAsync(NewId("br"), "not")))
            .Should().BeTrue(Because);
    }

    [Fact]
    public async Task ChatBridge_PublishBotMessageAsync()
    {
        var gate = Gate();
        var bridge = Bridge(gate);
        (await NonBlocking.ReturnsWhileDbIsBlockedAsync(gate, () => bridge.PublishBotMessageAsync(NewId("br"), "yanıt")))
            .Should().BeTrue(Because);
    }
}
