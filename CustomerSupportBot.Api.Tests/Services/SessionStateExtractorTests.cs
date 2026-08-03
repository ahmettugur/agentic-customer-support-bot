// Tests/Services/SessionStateExtractorTests.cs
//
// SessionStateExtractor, IdExtractor'ın sonucunu KALICI oturum durumuna yazar — bu yüzden
// buradaki bir yanlış sınıflandırma tek turluk bir hint hatası değil, oturumu zehirleyen
// kalıcı bir hatadır: state.CustomerId sonraki her turda EntityVerifier'a bir "kaynak" olarak
// ve CustomerContextProvider'a sipariş/şikayet geçmişi sorgusu olarak gider.
//
// Canlıda gözlemlenen hata: kullanıcı "Sipariş numaram 1041." dediğinde sipariş numarası
// state.CustomerId'ye yazılıyor, LastMentionedOrderId ise hiç set edilmiyordu.
// Bkz. IdExtractorTests — "numaram" sahiplenmesi.

using CustomerSupportBot.Domain.Model;
using CustomerSupportBot.Domain.Services;

namespace CustomerSupportBot.Api.Tests.Services;

public class SessionStateExtractorTests
{
    private static SessionState Apply(string userMessage, string botResponse = "")
    {
        var state = new SessionState();
        SessionStateExtractor.ExtractAndApply(state, userMessage, botResponse);
        return state;
    }

    /// <summary>
    /// Birden fazla turu, InMemorySessionManager/PostgresSessionManager.AddExchange'in
    /// yaptığı gibi sırayla uygular: her tur, KENDİSİNDEN ÖNCEKİ geçmiş snapshot'ıyla
    /// çağrılır, sonra kendi mesajları geçmişe eklenir.
    /// </summary>
    private static SessionState ApplyTurns(params (string User, string Bot)[] turns)
    {
        var state = new SessionState();
        var history = new List<ConversationMessage>();
        foreach (var (user, bot) in turns)
        {
            var priorSnapshot = new List<ConversationMessage>(history);
            SessionStateExtractor.ExtractAndApply(state, user, bot, priorSnapshot);
            history.Add(new ConversationMessage(ConversationRoles.User, user));
            history.Add(new ConversationMessage(ConversationRoles.Assistant, bot));
        }
        return state;
    }

    [Fact]
    public void ExtractAndApply_OrderQualifiedNumaram_DoesNotPoisonCustomerId()
    {
        var state = Apply("Sipariş numaram 1041.", "1041 numaralı siparişiniz kargoda.");

        state.CustomerId.Should().BeNull("sipariş numarası müşteri kimliği olarak kaydedilmemeli");
        state.CollectedInfo.Should().ContainKey("LastMentionedOrderId");
        state.CollectedInfo["LastMentionedOrderId"].Should().Be("1041");
    }

    [Fact]
    public void ExtractAndApply_CustomerNumaram_SetsCustomerId()
    {
        var state = Apply("müşteri numaram 1025");

        state.CustomerId.Should().Be("1025");
        state.CollectedInfo.Should().NotContainKey("LastMentionedOrderId");
    }

    [Fact]
    public void ExtractAndApply_CustomerIdFromBotResponse_OnlyWhenUserHasNone()
    {
        // Kullanıcı mesajında müşteri kimliği yoksa bot yanıtından türetilir.
        var state = Apply("teşekkürler", "Müşteri numaranız 1008 olarak kayıtlı.");
        state.CustomerId.Should().Be("1008");
    }

    [Fact]
    public void ExtractAndApply_UserCustomerIdWins_OverBotResponse()
    {
        var state = Apply("müşteri numaram 1025", "Müşteri numaranız 1008 olarak kayıtlı.");
        state.CustomerId.Should().Be("1025");
    }

    [Fact]
    public void ExtractAndApply_IncrementsTurnCountAndRecordsSentiment()
    {
        var state = Apply("Sipariş numaram 1041.");

        state.TurnCount.Should().Be(1);
        state.SentimentHistory.Should().ContainSingle();
        state.SentimentHistory[0].Turn.Should().Be(1);
    }

    [Fact]
    public void ExtractAndApply_SecondTurn_DoesNotOverwriteCustomerIdWithOrderNumber()
    {
        // Regresyon: 1. turda müşteri kimliği doğru alınır, 2. turda sipariş numarası
        // verilirse bu kimliğin ÜZERİNE yazılmamalı.
        var state = new SessionState();
        SessionStateExtractor.ExtractAndApply(state, "müşteri numaram 1025", "");
        SessionStateExtractor.ExtractAndApply(state, "Sipariş numaram 1041.", "");

        state.CustomerId.Should().Be("1025");
        state.CollectedInfo["LastMentionedOrderId"].Should().Be("1041");
    }

    // ─── Bağlamsız takip mesajının önceki tur bağlamını takip etmesi ────────────────
    // Canlıda gözlemlenen senaryo: "sipariş numaram 1030" turundan sonra kullanıcı sadece
    // "1030" (ya da "peki 1030") yazınca, priorHistory verilmezse IdExtractor bağlam
    // bulamayıp 1030'u customer_id sanar — sipariş numarası KALICI olarak
    // state.CustomerId'ye yazılır ve sonraki her turu zehirler. EntityVerifier için
    // yapılan düzeltme burada da (priorHistory üzerinden) geçerli olmalı.

    [Theory]
    [InlineData("1030")]
    [InlineData("peki 1030")]
    public void ExtractAndApply_AmbiguousFollowUp_AfterOrderContext_DoesNotPoisonCustomerId(string followUp)
    {
        var state = ApplyTurns(
            ("sipariş numaram 1030", "1030 numaralı siparişinizi kontrol ettim: Teslim Edildi."),
            (followUp, "1030 numaralı siparişiniz bulundu.")
        );

        state.CustomerId.Should().BeNull("1030 sipariş bağlamında yorumlanmalı, müşteri kimliği olarak kaydedilmemeli");
        state.CollectedInfo.Should().ContainKey("LastMentionedOrderId");
        state.CollectedInfo["LastMentionedOrderId"].Should().Be("1030");
    }

    [Fact]
    public void ExtractAndApply_AmbiguousFollowUp_AfterComplaintContext_ResolvesAsOrderId()
    {
        var state = ApplyTurns(
            ("şikayet numaram 1001", "1001 numaralı şikayetiniz inceleniyor."),
            ("peki 1001", "1001 numaralı şikayetiniz hâlâ inceleniyor.")
        );

        state.CustomerId.Should().BeNull();
        // SessionState şikayet ID'sini ayrıca takip etmiyor (yalnızca LastMentionedOrderId
        // vardır) — burada asıl doğrulanan customer_id'nin zehirlenmediğidir.
    }

    [Fact]
    public void ExtractAndApply_AmbiguousQuery_NoPriorHistory_StillDefaultsToCustomerId()
    {
        // Geriye dönük uyumluluk: priorHistory verilmezse (varsayılan null) eski davranış
        // korunur — bu, yeni parametrenin var olan çağıranları bozmadığını da doğrular.
        var state = Apply("1008");
        state.CustomerId.Should().Be("1008");
    }

    [Fact]
    public void ExtractAndApply_ExplicitCustomerMessage_NeverReclassifiedByPriorOrderContext()
    {
        var state = ApplyTurns(
            ("sipariş numaram 1030", "1030 numaralı siparişinizi kontrol ettim."),
            ("müşteri 1008 bilgisi", "")
        );

        state.CustomerId.Should().Be("1008");
    }
}
