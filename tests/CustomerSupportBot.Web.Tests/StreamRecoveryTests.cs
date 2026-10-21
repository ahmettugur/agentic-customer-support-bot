// Akış koptuğunda ya da sunucu hata bildirdiğinde: tur sunucuda yine de tamamlanıp kaydedildiyse yanıt geçmişten alınır.
// Yanlış pozitif ("eski bir yanıtı yeni sanmak") en kötü sonuçtur; bu yüzden eşleşme sıkıdır ve "yeni" olması BAŞLANGIÇ
// mesaj sayısına göre doğrulanır (aynı soru daha önce de sorulmuş olabilir).

using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class StreamRecoveryTests
{
    private static SessionMessage U(string t) => new("user", t);
    private static SessionMessage B(string t) => new("bot", t);

    [Fact]
    public void TheTurnCompletedOnTheServer_ReturnsTheAnswer() =>
        StreamRecovery.FindCompletedAnswer([U("merhaba"), B("selam")], baselineCount: 0, "merhaba").Should().Be("selam");

    [Fact]
    public void WorksAfterEarlierTurns()
    {
        var server = new[] { U("a"), B("1"), U("b"), B("2"), U("siparişim nerede"), B("Kargoda.") };

        StreamRecovery.FindCompletedAnswer(server, baselineCount: 4, "siparişim nerede").Should().Be("Kargoda.");
    }

    [Fact]
    public void NothingNewOnTheServer_ReturnsNull() =>
        StreamRecovery.FindCompletedAnswer([U("a"), B("1")], baselineCount: 2, "a").Should().BeNull("eski bir çift yeni sanılmamalı");

    [Fact]
    public void AnOlderIdenticalQuestion_IsNotMistakenForTheFailedTurn()
    {
        // Müşteri aynı soruyu daha önce de sormuş; bu deneme sunucuda tamamlanmadı.
        var server = new[] { U("siparişim nerede"), B("eski yanıt"), U("başka"), B("x") };

        StreamRecovery.FindCompletedAnswer(server, baselineCount: 4, "siparişim nerede").Should().BeNull();
    }

    [Fact]
    public void TheServerSavedOnlyTheUsersMessage_ReturnsNull() =>
        StreamRecovery.FindCompletedAnswer([U("merhaba")], baselineCount: 0, "merhaba").Should().BeNull();

    [Fact]
    public void TheLastQuestionIsDifferent_ReturnsNull() =>
        StreamRecovery.FindCompletedAnswer([U("başka bir şey"), B("yanıt")], baselineCount: 0, "merhaba").Should().BeNull();

    [Fact]
    public void LineEndingsAndSpacingDoNotMatter() =>
        StreamRecovery.FindCompletedAnswer([U("iade  nasıl\nyapılır"), B("şöyle")], baselineCount: 0, " iade nasıl\r\nyapılır ").Should().Be("şöyle");

    [Fact]
    public void AnEmptyAnswer_ReturnsNull() =>
        StreamRecovery.FindCompletedAnswer([U("merhaba"), B("   ")], baselineCount: 0, "merhaba").Should().BeNull();

    [Fact]
    public void EmptyServerList_ReturnsNull() =>
        StreamRecovery.FindCompletedAnswer([], baselineCount: 0, "merhaba").Should().BeNull();
}
