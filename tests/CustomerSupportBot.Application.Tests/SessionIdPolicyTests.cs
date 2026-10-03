using CustomerSupportBot.Application.Services.Chat;

namespace CustomerSupportBot.Application.Tests;

public class SessionIdPolicyTests
{
    [Theory]
    [InlineData("3f2b8c1e-7d4a-4e5b-9c6f-1a2b3c4d5e6f")] // sunucunun ürettiği biçim
    [InlineData("session-1")]
    [InlineData("a_b-C9")]
    public void AcceptsIdsTheSystemProduces(string id) =>
        SessionIdPolicy.IsValid(id).Should().BeTrue();

    [Fact]
    public void AcceptsExactlyTheColumnLength() =>
        SessionIdPolicy.IsValid(new string('a', SessionIdPolicy.MaxLength)).Should().BeTrue();

    [Fact]
    public void RejectsLongerThanTheColumn() =>
        SessionIdPolicy.IsValid(new string('a', SessionIdPolicy.MaxLength + 1)).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../etc/passwd")]
    [InlineData("oturum 1")]
    [InlineData("id<script>")]
    [InlineData("şube")]
    public void RejectsEmptyOrUnexpectedCharacters(string id) =>
        SessionIdPolicy.IsValid(id).Should().BeFalse();
}
