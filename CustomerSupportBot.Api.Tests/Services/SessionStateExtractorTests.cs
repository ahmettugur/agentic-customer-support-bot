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
}
