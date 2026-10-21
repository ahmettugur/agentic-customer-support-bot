// 👍/👎 çubuğunun saf kuralları: aynı düğmeye tekrar basmak oyu geri çeker; neden etiketleri sabit kümeden.

using CustomerSupportBot.Web.Services;

namespace CustomerSupportBot.Web.Tests;

public class FeedbackUiTests
{
    [Theory]
    [InlineData(null, true, true)]      // oy yok → 👍
    [InlineData(null, false, false)]
    [InlineData(true, true, null)]      // 👍'e tekrar bas → geri çek
    [InlineData(false, false, null)]
    [InlineData(true, false, false)]    // 👍'den 👎'e geç
    [InlineData(false, true, true)]
    public void NextVote_TogglesOffOnRepeat(bool? current, bool pressed, bool? expected) =>
        FeedbackUi.NextVote(current, pressed).Should().Be(expected);

    [Fact]
    public void Reasons_AreTheServersClosedSet_WithTurkishLabels()
    {
        FeedbackUi.Reasons.Select(r => r.Code).Should().Equal("wrong", "irrelevant", "unclear", "other");
        FeedbackUi.Reasons.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Label));
    }

    [Fact]
    public void ReasonsOffered_OnlyAfterADownvote_UntilOneIsChosen()
    {
        FeedbackUi.ShowReasons(vote: false, reason: null).Should().BeTrue();
        FeedbackUi.ShowReasons(vote: false, reason: "wrong").Should().BeFalse("seçildi, teşekkür gösterilir");
        FeedbackUi.ShowReasons(vote: true, reason: null).Should().BeFalse();
        FeedbackUi.ShowReasons(vote: null, reason: null).Should().BeFalse();
    }

    [Theory]
    [InlineData("assistant", false, "Merhaba", "s1", true)]
    [InlineData("assistant", true, "Merhaba", "s1", false)]     // akıyor
    [InlineData("assistant", false, "", "s1", false)]           // boş
    [InlineData("assistant", false, "Merhaba", null, false)]    // oturum henüz yok
    [InlineData("user", false, "Merhaba", "s1", false)]
    [InlineData("human", false, "Merhaba", "s1", false)]        // temsilci mesajına bot oyu verilmez
    public void CanVote_OnlyOnFinishedBotAnswers(string role, bool streaming, string content, string? session, bool expected) =>
        FeedbackUi.CanVote(role, streaming, content, session, isVoiceBubble: false).Should().Be(expected);

    [Fact]
    public void CanVote_NotOnVoiceBubbles() =>
        FeedbackUi.CanVote("assistant", false, "Merhaba", "s1", isVoiceBubble: true).Should().BeFalse();
}
