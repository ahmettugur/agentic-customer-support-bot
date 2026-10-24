// E-posta yanıtından yalnızca müşterinin YENİ yazdığı kısım: alıntılar, imza ve "… yazdı:" satırları bota gitmemeli.

using CustomerSupportBot.Application.Services.Channels;

namespace CustomerSupportBot.Application.Tests;

public class EmailReplyExtractorTests
{
    [Fact]
    public void APlainMessage_IsKept() =>
        EmailReplyExtractor.ExtractNewText("Siparişim nerede?").Should().Be("Siparişim nerede?");

    [Fact]
    public void EnglishQuoteHeader_CutsEverythingAfterIt() =>
        EmailReplyExtractor.ExtractNewText("Teşekkürler, iade etmek istiyorum.\n\nOn Mon, Oct 21, 2026 at 9:00 AM Destek <d@x.com> wrote:\n> eski mesaj")
            .Should().Be("Teşekkürler, iade etmek istiyorum.");

    [Fact]
    public void TheEnglishHeader_WrappedOverTwoLines_IsCutToo() =>
        EmailReplyExtractor.ExtractNewText("Tamam.\n\nOn Mon, Oct 21, 2026 at 9:00 AM Müşteri Destek Asistanı\n<destek@x.com> wrote:\n> eski")
            .Should().Be("Tamam.");

    [Fact]
    public void TurkishQuoteHeader_IsCut() =>
        EmailReplyExtractor.ExtractNewText("Kargo gelmedi.\n\n21 Eki 2026 Pzt 09:00 tarihinde Destek <d@x.com> şunu yazdı:\n> eski")
            .Should().Be("Kargo gelmedi.");

    [Theory]
    [InlineData("Merhaba\n\n-----Original Message-----\nFrom: x\nSent: y")]
    [InlineData("Merhaba\n\n----- Orijinal Mesaj -----\nGönderen: x")]
    [InlineData("Merhaba\n\n________________________________\nFrom: Destek\nSent: Monday\nTo: me\nSubject: x")]
    public void OutlookStyleSeparators_AreCut(string raw) =>
        EmailReplyExtractor.ExtractNewText(raw).Should().Be("Merhaba");

    [Fact]
    public void SignatureDelimiter_IsCut() =>
        EmailReplyExtractor.ExtractNewText("Mesajım bu.\n-- \nAhmet T.\n0532 000 00 00").Should().Be("Mesajım bu.");

    [Fact]
    public void SentFromMyPhone_IsCut() =>
        EmailReplyExtractor.ExtractNewText("Mesajım bu.\n\nSent from my iPhone\n").Should().Be("Mesajım bu.");

    [Fact]
    public void QuotedLines_AreDropped_EvenWithoutAHeader() =>
        EmailReplyExtractor.ExtractNewText("Cevabım:\n> alıntı satırı\n> bir tane daha\nDevamı").Should().Be("Cevabım:\nDevamı");

    [Fact]
    public void WindowsLineEndings_AreNormalised() =>
        EmailReplyExtractor.ExtractNewText("Bir\r\nİki\r\n").Should().Be("Bir\nİki");

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData("> sadece alıntı")]
    [InlineData("-----Original Message-----\nFrom: x")]
    public void NothingNew_YieldsAnEmptyString(string raw) =>
        EmailReplyExtractor.ExtractNewText(raw).Should().BeEmpty();

    [Fact]
    public void TheResult_IsClippedToTheLimit() =>
        EmailReplyExtractor.ExtractNewText(new string('a', 5000), maxLength: 100).Should().HaveLength(100);

    [Theory]
    [InlineData("noreply@x.com", "Re: x", null, true)]
    [InlineData("no-reply@x.com", "Re: x", null, true)]
    [InlineData("mailer-daemon@x.com", "Undelivered", null, true)]
    [InlineData("ayse@x.com", "Out of office: x", null, true)]
    [InlineData("ayse@x.com", "Otomatik yanıt: x", null, true)]
    [InlineData("ayse@x.com", "Re: x", "auto-replied", true)]
    [InlineData("ayse@x.com", "Re: x", "no", false)]
    [InlineData("ayse@x.com", "Re: x", null, false)]
    public void AutomatedMail_IsRecognised(string from, string subject, string? autoSubmitted, bool expected) =>
        EmailReplyExtractor.IsAutomated(from, subject, autoSubmitted).Should().Be(expected);
}
