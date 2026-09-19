// Tests/Services/PiiMaskerTests.cs

using CustomerSupportBot.Application.Services.Logging;

namespace CustomerSupportBot.Application.Tests;

public class PiiMaskerTests
{
    [Theory]
    [InlineData("ahmet@example.com", "a***@example.com")]
    [InlineData("a@x.com", "a***@x.com")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("not-an-email", "***")]
    public void MaskEmail_MasksLocalPartOnly(string? input, string expected)
        => PiiMasker.MaskEmail(input).Should().Be(expected);

    [Theory]
    [InlineData("05321234567", "*********67")]
    [InlineData("+90 532 123 45 67", "+** *** *** ** 67")]
    [InlineData(null, "")]
    public void MaskPhone_KeepsLastTwoDigitsAndFormatting(string? input, string expected)
        => PiiMasker.MaskPhone(input).Should().Be(expected);

    [Fact]
    public void MaskTcKimlikNo_KeepsLastTwoDigits()
        => PiiMasker.MaskTcKimlikNo("12345678901").Should().Be("*********01");

    [Theory]
    [InlineData("4111 1111 1111 1111", "**** **** **** 1111")]
    [InlineData("4111-1111-1111-1111", "****-****-****-1111")]
    public void MaskCreditCard_KeepsLastFourDigits(string input, string expected)
        => PiiMasker.MaskCreditCard(input).Should().Be(expected);

    [Theory]
    [InlineData("203.0.113.45", "203.0.113.*")]
    [InlineData("2001:db8:85a3::8a2e:370:7334", "2001:db8::")]
    [InlineData(null, "")]
    public void MaskIpAddress_MasksHostPortion(string? input, string expected)
        => PiiMasker.MaskIpAddress(input).Should().Be(expected);
}
