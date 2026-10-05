// Oturum durumu — pod'lar arası eşzamanlı yazma (gerçek Postgres). Eskiden yazma "son yazan kazanır"dı: bayat
// önbellekli bir pod tüm StateJson'ı yazınca başka pod'un yaptığı değişiklik (ör. HumanInvolved) kayboluyordu.
// Pod'lar burada pub/sub ile bağlı DEĞİL (Redis en-fazla-bir-kez: mesaj kaybolmuş gibi); ortak olan yalnızca
// dağıtık kilit ve veritabanı.

using CustomerSupportBot.Adapters.Persistence.Postgres;
using CustomerSupportBot.Adapters.Redis;
using CustomerSupportBot.Application.Services.Chat;
using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

[Collection("PostgresCatalog")]
public class PostgresSessionConcurrentWriteTests(PostgresCatalogFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryDistributedLock _locks = new(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));

    private PostgresSessionManager Pod() =>
        new(fixture.DbFactory, _locks, new NoopMessageBus(), NullLogger<PostgresSessionManager>.Instance);

    private static string NewSessionId() => $"cw-{Guid.NewGuid():N}";

    private async Task<SessionState> StateInDbAsync(string sid) =>
        (await Pod().ReloadAsync(sid, Ct)).State;

    [Fact]
    public async Task TurnEndOnAStalePod_KeepsTheFlagSetByAnotherPod()
    {
        var sid = NewSessionId();
        var (a, b) = (Pod(), Pod());
        await a.GetOrCreateAsync(sid, Ct);
        await a.AddExchangeAsync(sid, "merhaba", "buyrun", ct: Ct);
        await b.GetAsync(sid, Ct);   // b önbelleğe aldı — bundan sonra a'nın yazımını duymayacak

        await a.MutateStateAsync(sid, s => s.HumanInvolved = true, Ct);
        await b.AddExchangeAsync(sid, "siparişim nerede", "kontrol ediyorum", ct: Ct);

        var state = await StateInDbAsync(sid);
        state.HumanInvolved.Should().BeTrue("bayat pod tur sonunda tüm durumu yazınca bayrağı silmemeli");
        state.TurnCount.Should().Be(2, "iki turun ikisi de sayılmalı");
    }

    [Fact]
    public async Task MutationsFromTwoStalePods_BothSurvive()
    {
        var sid = NewSessionId();
        var (a, b) = (Pod(), Pod());
        await a.GetOrCreateAsync(sid, Ct);
        await a.AddExchangeAsync(sid, "merhaba", "buyrun", ct: Ct);
        await b.GetAsync(sid, Ct);

        await a.MutateStateAsync(sid, s => s.ReplanNote = "iade sürecini kontrol et", Ct);
        await b.MutateStateAsync(sid, s => s.ConversationSummary = "Müşteri iade istiyor.", Ct);

        var state = await StateInDbAsync(sid);
        state.ReplanNote.Should().Be("iade sürecini kontrol et");
        state.ConversationSummary.Should().Be("Müşteri iade istiyor.");
    }

    [Fact]
    public async Task ConcurrentMutations_OnTheSameField_AllApply()
    {
        var sid = NewSessionId();
        var pods = Enumerable.Range(0, 4).Select(_ => Pod()).ToList();
        await pods[0].GetOrCreateAsync(sid, Ct);
        await pods[0].AddExchangeAsync(sid, "merhaba", "buyrun", ct: Ct);
        foreach (var p in pods) await p.GetAsync(sid, Ct);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            pods[i % pods.Count].MutateStateAsync(sid, s => s.ConsecutiveNegativeTurns++, Ct)));

        (await StateInDbAsync(sid)).ConsecutiveNegativeTurns.Should().Be(20, "hiçbir artış kaybolmamalı");
    }

    [Fact]
    public async Task BindingAnUnownedSession_FromTwoStalePods_TheFirstCustomerKeepsIt()
    {
        var sid = NewSessionId();
        var (a, b) = (Pod(), Pod());
        await a.GetOrCreateAsync(sid, Ct);
        await a.AddExchangeAsync(sid, "merhaba", "buyrun", ct: Ct);
        var onB = await b.GetAsync(sid, Ct);

        (await SessionIdentityBinder.TryBindAsync(await a.GetOrCreateAsync(sid, Ct), "1001", a, Ct)).Should().BeTrue();
        (await SessionIdentityBinder.TryBindAsync(onB!, "2002", b, Ct))
            .Should().BeFalse("b'nin önbelleği oturumu sahipsiz sanıyor ama kilit altında 1001'in bağladığını görmeli");

        (await StateInDbAsync(sid)).AuthenticatedCustomerId.Should().Be("1001");
    }
}
