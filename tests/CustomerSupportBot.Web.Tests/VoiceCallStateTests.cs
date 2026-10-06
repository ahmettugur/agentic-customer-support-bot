using CustomerSupportBot.Web.Helpers;

namespace CustomerSupportBot.Web.Tests;

public class VoiceCallStateTests
{
    [Theory]
    [InlineData("ringing", null, "Aranıyor…")]
    [InlineData("active", null, "Görüşmede")]
    [InlineData("declined", "no_microphone", "Müşterinin mikrofonu yok")]
    [InlineData("declined", "declined", "Müşteri reddetti")]
    [InlineData("missed", "missed", "Cevap verilmedi")]
    [InlineData("failed", "connection_lost", "Bağlantı koptu")]
    [InlineData("ended", "customer_hangup", "Görüşme bitti")]
    public void StatusLabel(string status, string? reason, string expected) =>
        VoiceCallText.StatusLabel(status, reason).Should().Be(expected);

    [Fact]
    public void FormatElapsed_IsMinutesSeconds() =>
        VoiceCallText.FormatElapsed(TimeSpan.FromSeconds(252)).Should().Be("04:12");

    [Theory]
    [InlineData("voice_call_busy", "Zaten bir sesli görüşmedesiniz.")]
    [InlineData("not_in_human_mode", "Sesli görüşme için önce sohbeti devralın.")]
    [InlineData(null, "Sesli görüşme başlatılamadı.")]
    public void StartError(string? code, string expected) =>
        VoiceCallText.StartError(code).Should().Be(expected);

    [Theory]
    [InlineData("declined", null, "Müşteri reddetti")]
    [InlineData("declined", "no_microphone", "Müşterinin mikrofonu yok")]
    [InlineData("ended", "missed", "Cevap verilmedi")]
    [InlineData("ended", "customer_hangup", null)]
    [InlineData("accepted", null, null)]
    public void EndNotice_OnlyForCallsThatNeverConnected(string type, string? reason, string? expected) =>
        VoiceCallText.EndNotice(type, reason).Should().Be(expected);
}
