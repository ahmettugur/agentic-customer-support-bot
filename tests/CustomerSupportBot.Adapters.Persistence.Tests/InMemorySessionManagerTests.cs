// Tests/Services/InMemorySessionManagerTests.cs

using CustomerSupportBot.Adapters.Redis;
using Microsoft.Extensions.Options;
using CustomerSupportBot.Tests.Shared;
using CustomerSupportBot.Adapters.Persistence.InMemory;
using CustomerSupportBot.Application.Ports.Outbound.Locking;

namespace CustomerSupportBot.Adapters.Persistence.Tests;

public class InMemorySessionManagerTests
{
    private static readonly IAppDistributedLock _lock =
        new InMemoryDistributedLock(Options.Create(new RedisOptions { DefaultLockTimeoutSeconds = 10 }));
    private readonly InMemorySessionManager _mgr = new(_lock);

    [Fact]
    public async Task GetOrCreateSession_NewId_CreatesSession()
    {
        var s = await _mgr.GetOrCreateAsync("s1", TestContext.Current.CancellationToken);
        s.Should().NotBeNull();
        s.SessionId.Should().Be("s1");
        s.State.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrCreateSession_ExistingId_ReturnsSame()
    {
        var s1 = await _mgr.GetOrCreateAsync("s1", TestContext.Current.CancellationToken);
        var s2 = await _mgr.GetOrCreateAsync("s1", TestContext.Current.CancellationToken);
        s1.Should().BeSameAs(s2);
    }

    [Fact]
    public async Task GetOrCreateSession_NullId_GeneratesNewId()
    {
        var s = await _mgr.GetOrCreateAsync(null, TestContext.Current.CancellationToken);
        s.SessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetSession_Unknown_ReturnsNull()
    {
        (await _mgr.GetAsync("unknown", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task AddExchange_BuildsHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mgr.AddExchangeAsync("s1", "merhaba", "selam", ct: ct);
        await _mgr.AddExchangeAsync("s1", "yardım", "tabii", ct: ct);

        var history = await _mgr.GetHistoryAsync("s1", ct);
        history.Should().HaveCount(4);
        history[0].Text.Should().Be("merhaba");
        history[1].Text.Should().Be("selam");
    }

    [Fact]
    public async Task GetHistory_UnknownSession_EmptyList()
    {
        (await _mgr.GetHistoryAsync("unknown", TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractAndUpdateState_CapturesCustomerId()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mgr.AddExchangeAsync("s1", "müşteri numaram 1027", "merhaba", ct: ct);
        var s = await _mgr.GetAsync("s1", ct);
        s!.State.CustomerId.Should().Be("1027");
    }

    [Fact]
    public async Task ExtractAndUpdateState_CapturesOrderId()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mgr.AddExchangeAsync("s1", "sipariş 1030 nerede?", "siparişiniz yolda", ct: ct);
        var s = await _mgr.GetAsync("s1", ct);
        s!.State.CollectedInfo.Should().ContainKey("LastMentionedOrderId");
    }

    [Fact]
    public async Task AddExchange_AmbiguousFollowUp_AfterOrderContext_DoesNotPoisonCustomerId()
    {
        // Uçtan uca kanıt: AddExchange, ExtractAndUpdateState/ExtractAndUpdateStateCore
        // zincirinde geçmişi gerçekten SessionStateExtractor'a taşıyor mu?
        // Canlıda gözlemlenen senaryo: "sipariş numaram 1030" turundan sonra kullanıcı
        // sadece "1030" yazarsa, sipariş numarası state.CustomerId'ye zehirlenmemeli.
        // İkinci turun bot yanıtı BİLİNÇLİ olarak nötr ("sipariş"/"müşteri"/"şikayet"
        // kelimesi içermez) — SessionStateExtractor'da AYRI bir mekanizma (bot yanıtından
        // customer_id türetme, bkz. ExtractAndApply_CustomerIdFromBotResponse_OnlyWhenUserHasNone)
        // ve SessionState'in "yalnızca en son set edilen alan gözlemlenebilir" doğası, bu
        // testin tam olarak snapshot ZAMANLAMASINI (ekleme öncesi/sonrası) izole eden bir
        // mutasyon-öldürücü olmasını engelliyor — o ince ayrıntı EntityVerifier'daki
        // (zaten mutasyonla doğrulanmış) aynı desenle ve kod incelemesiyle güvence altında.
        // Bu test asıl regresyon sınıfını kanıtlıyor: priorHistory gerçekten iletiliyor mu.
        var ct = TestContext.Current.CancellationToken;
        await _mgr.AddExchangeAsync("s1", "sipariş numaram 1030", "1030 numaralı siparişinizi kontrol ettim.", ct: ct);
        await _mgr.AddExchangeAsync("s1", "1030", "Bir saniye, kontrol ediyorum.", ct: ct);

        var s = await _mgr.GetAsync("s1", ct);
        s!.State.CustomerId.Should().BeNull("1030 sipariş bağlamında yorumlanmalı");
        s.State.CollectedInfo["LastMentionedOrderId"].Should().Be("1030");
    }

    [Fact]
    public async Task AddExchange_IncrementsTurnCount()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mgr.AddExchangeAsync("s1", "msg1", "r1", ct: ct);
        await _mgr.AddExchangeAsync("s1", "msg2", "r2", ct: ct);
        (await _mgr.GetAsync("s1", ct))!.State.TurnCount.Should().Be(2);
    }

    [Fact]
    public async Task ClearSession_RemovesData()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mgr.AddExchangeAsync("s1", "x", "y", ct: ct);
        await _mgr.ClearSessionAsync("s1", ct);
        (await _mgr.GetAsync("s1", ct)).Should().BeNull();
        (await _mgr.GetHistoryAsync("s1", ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAssistantMessage_AppendsToHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mgr.GetOrCreateAsync("s1", ct);
        await _mgr.AppendAssistantMessageAsync("s1", "agent yanıtı", ct);
        var h = await _mgr.GetHistoryAsync("s1", ct);
        h.Should().HaveCount(1);
        h[0].Text.Should().Be("agent yanıtı");
    }

    [Fact]
    public async Task AppendAssistantMessage_FillsEmptyPlaceholder()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mgr.AddExchangeAsync("s1", "user", "", ct: ct);
        await _mgr.AppendAssistantMessageAsync("s1", "real reply", ct);
        var h = await _mgr.GetHistoryAsync("s1", ct);
        h.Should().HaveCount(2);
        h[1].Text.Should().Be("real reply");
    }

    [Fact]
    public async Task UpdateSession_UpdatesLastActivity()
    {
        var ct = TestContext.Current.CancellationToken;
        var s = await _mgr.GetOrCreateAsync("s1", ct);
        var before = s.LastActivity;
        Thread.Sleep(5);
        await _mgr.UpdateAsync(s, ct);
        s.LastActivity.Should().BeAfter(before);
    }
}
