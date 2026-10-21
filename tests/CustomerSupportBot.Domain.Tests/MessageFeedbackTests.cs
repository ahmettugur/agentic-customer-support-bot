using CustomerSupportBot.Domain.Model;

namespace CustomerSupportBot.Domain.Tests;

public class MessageFeedbackTests
{
    [Fact]
    public void HashOf_IsStable_AndIgnoresLineEndingsAndOuterWhitespace()
    {
        Assert.Equal(MessageFeedback.HashOf("a\nb"), MessageFeedback.HashOf("  a\r\nb \n"));
    }

    [Fact]
    public void HashOf_DiffersForDifferentText() => Assert.NotEqual(MessageFeedback.HashOf("a"), MessageFeedback.HashOf("b"));

    [Fact]
    public void HashOf_IsShortLowercaseHex() => Assert.Matches("^[0-9a-f]{16}$", MessageFeedback.HashOf("x"));

    [Theory]
    [InlineData("wrong", true)]
    [InlineData("irrelevant", true)]
    [InlineData("unclear", true)]
    [InlineData("other", true)]
    [InlineData("WRONG", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("<script>", false)]
    public void Reasons_AreAClosedSet(string? reason, bool valid) => Assert.Equal(valid, MessageFeedbackReasons.IsValid(reason));
}
